using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// Turns raw <see cref="IArchipelagoSession"/> events (chat/log lines, item
/// receipts, hint updates, location progress) into calls on
/// <see cref="IMessageHistoryService"/>/<see cref="IHintService"/> - the
/// mechanism behind Phase 6's passive sibling-slot coverage. Split out of
/// <see cref="ConnectionManager"/>, which still owns session lifecycle
/// (<c>_sessions</c>/<c>_leaderSlotByGroup</c>/locking) and calls into this
/// class from inside session event subscriptions. See Umsetzungsplan.md,
/// section "ConnectionManager: Locking, Nebenläufigkeit und Workarounds im
/// Detail" for the underlying design rationale (BuildSlotRoster/Alias-
/// Refresh, TrackHints-per-sibling, item-backlog-vs-live, ...).
/// </summary>
internal sealed class SessionEventTranslator
{
    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;

    // Keyed by SlotProfile.Id; that slot's most recently learned "missing
    // locations" for the Hint picker's Location mode. See Umsetzungsplan.md,
    // section "_missingLocationsBySlot: bewusst nur im Speicher".
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<HintableLocation>> _missingLocationsBySlot = new();

    public SessionEventTranslator(IMessageHistoryService messageHistoryService, IHintService hintService)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
    }

    /// <summary>
    /// Fast, lock-free read of <paramref name="slotId"/>'s cached missing
    /// locations - see <see cref="GetHintableLocationsAsync"/> in
    /// <see cref="ConnectionManager"/> and Umsetzungsplan.md, section
    /// "GetHintableLocationsAsync: Fast-Path und Re-Check".
    /// </summary>
    public bool TryGetMissingLocations(Guid slotId, out IReadOnlyList<HintableLocation> locations) =>
        _missingLocationsBySlot.TryGetValue(slotId, out locations!);

    /// <summary>
    /// Maps every configured slot to its numeric Archipelago slot id, by
    /// matching <see cref="SlotProfile.SlotName"/> against the room roster.
    /// Rebuilt on every call rather than cached. Side effect: also refreshes
    /// <see cref="SlotProfile.Alias"/> for every matched slot. See
    /// Umsetzungsplan.md, section "BuildSlotRoster und Alias-Refresh".
    /// </summary>
    public static Dictionary<int, SlotProfile> BuildSlotRoster(IArchipelagoSession session, ServerConnectionGroup serverGroup)
    {
        var roster = new Dictionary<int, SlotProfile>();
        foreach (var slot in serverGroup.Slots)
        {
            var match = session.Players.AllPlayers.FirstOrDefault(p =>
                string.Equals(p.Name, slot.SlotName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                roster[match.Slot] = slot;

                if (!string.Equals(slot.Alias, match.Alias, StringComparison.Ordinal))
                {
                    var alias = match.Alias;
                    Dispatcher.UIThread.Post(() => slot.Alias = alias);
                }
            }
        }

        return roster;
    }

    /// <summary>
    /// Adds one more <c>TrackHints</c> subscription to the leader's
    /// already-open session, for a slot added after the leader connected.
    /// See Umsetzungsplan.md, section "Warum die Leader-Session pro
    /// Sibling-Slot ein eigenes TrackHints braucht". The caller
    /// (<see cref="ConnectionManager"/>) already looked <paramref name="leaderSession"/>
    /// up from its own <c>_sessions</c> map - this class has no session
    /// registry of its own.
    /// </summary>
    public void TrackHintsForSiblingOnLeader(GroupViewModel group, IArchipelagoSession leaderSession, SlotProfile newSlot)
    {
        var roster = BuildSlotRoster(leaderSession, group.Group);
        foreach (var (numericSlotId, matchedSlot) in roster)
        {
            if (matchedSlot.Id == newSlot.Id)
            {
                leaderSession.Hints.TrackHints(
                    hints => OnHintsUpdated(group, leaderSession, hints),
                    retrieveCurrentlyUnlockedHints: true,
                    slot: numericSlotId);
                return;
            }
        }
    }

    private static HashSet<int> SiblingIdsExcludingOwn(Dictionary<int, SlotProfile> roster, int ownNumericSlotId) =>
        new(roster.Keys.Where(id => id != ownNumericSlotId));

    /// <summary>
    /// Which single configured slot (if any) a log/chat message is "about",
    /// for the merged event log's slot filter. See Umsetzungsplan.md,
    /// section "ResolvePrimarySlotId".
    /// </summary>
    private static Guid? ResolvePrimarySlotId(LogMessage logMessage, Dictionary<int, SlotProfile> roster)
    {
        if (logMessage is ItemSendLogMessage send && roster.TryGetValue(send.Receiver.Slot, out var receivingSlot))
        {
            return receivingSlot.Id;
        }

        foreach (var part in logMessage.Parts)
        {
            if (part is PlayerMessagePart playerPart && roster.TryGetValue(playerPart.SlotId, out var matched))
            {
                return matched.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Fires for every chat/log line the leader's session receives, which
    /// covers the whole room - the core mechanism behind Phase 6. Mirrors an
    /// item-send broadcast into a non-leader slot's own received-items
    /// panel so that slot stays current without its own connection.
    /// </summary>
    public void OnLeaderMessageReceived(GroupViewModel group, IArchipelagoSession session, SlotProfile leaderSlot, LogMessage logMessage)
    {
        var roster = BuildSlotRoster(session, group.Group);
        var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);

        var segments = EventSegmentBuilder.BuildChatSegments(logMessage, siblingIds);
        var slotId = ResolvePrimarySlotId(logMessage, roster);
        var eventType = logMessage is ItemSendLogMessage ? EventType.ItemReceived : EventType.Chat;
        _messageHistoryService.HandleChatMessage(group, logMessage.ToString(), segments, slotId, eventType);

        // ItemSendLogMessage exposes the receiving side as .Receiver (a
        // PlayerInfo), not .Receiving - confirmed against the installed
        // Archipelago.MultiClient.Net 6.7.1 XML docs.
        if (logMessage is ItemSendLogMessage itemSend &&
            itemSend.Receiver.Slot != session.ConnectionInfo.Slot &&
            roster.TryGetValue(itemSend.Receiver.Slot, out var targetSlot))
        {
            var senderKind = EventSegmentBuilder.ClassifyPlayerSlot(itemSend.Item.Player, session.ConnectionInfo.Slot, siblingIds);
            var senderName = session.Players.GetPlayerAlias(itemSend.Item.Player) ?? string.Empty;
            _messageHistoryService.HandleObservedItemForSlot(
                group, targetSlot,
                itemSend.Item.ItemDisplayName, itemSend.Item.LocationDisplayName, itemSend.Item.Flags,
                senderName, senderKind);
        }
    }

    public void OnItemReceived(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, ReceivedItemsHelper helper, bool isLeaderSession, bool hasAnnouncedConnection)
    {
        // A catch-up session always treats everything as backlog. See
        // Umsetzungsplan.md, section "Item-Backlog vs. Live bei
        // OnItemReceived".
        var isLive = isLeaderSession && hasAnnouncedConnection;

        if (isLive)
        {
            // Already connected - the chat-log "X sent Y to Z" line handles
            // announcing this one; just keep the persisted index moving.
            _messageHistoryService.AdvanceItemSyncState(slot, helper.AllItemsReceived);
        }
        else
        {
            _messageHistoryService.HandleItemsReceivedSinceLastConnection(group, slot, helper.AllItemsReceived);
        }

        var senderName = string.Empty;
        var senderKind = EventTextSegmentKind.OtherSlotName;
        if (helper.AllItemsReceived.Count > 0)
        {
            var latest = helper.AllItemsReceived[helper.AllItemsReceived.Count - 1];
            senderName = session.Players.GetPlayerAlias(latest.Player) ?? string.Empty;
            var roster = BuildSlotRoster(session, group.Group);
            var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);
            senderKind = EventSegmentBuilder.ClassifyPlayerSlot(latest.Player, session.ConnectionInfo.Slot, siblingIds);
        }

        _messageHistoryService.TrackReceivedItem(group, slot, helper.AllItemsReceived, senderName, senderKind);

        // Drain the queue as documented by the library; the calls above
        // already read everything they need from AllItemsReceived.
        while (helper.Any())
        {
            helper.DequeueItem();
        }
    }

    /// <summary>
    /// Fires whenever one tracked slot's full current hint list is
    /// available - not room-wide by itself. See Umsetzungsplan.md, section
    /// "Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints
    /// braucht".
    /// </summary>
    public void OnHintsUpdated(GroupViewModel group, IArchipelagoSession session, Hint[] hints)
    {
        var roster = BuildSlotRoster(session, group.Group);
        if (roster.Count == 0 || hints.Length == 0)
        {
            return;
        }

        var ownSlotNumeric = session.ConnectionInfo.Slot;
        var siblingIds = SiblingIdsExcludingOwn(roster, ownSlotNumeric);

        var snapshots = new List<HintSnapshot>(hints.Length);

        foreach (var hint in hints)
        {
            Guid? slotId = null;
            if (roster.TryGetValue(hint.ReceivingPlayer, out var receivingSlot))
            {
                slotId = receivingSlot.Id;
            }
            else if (roster.TryGetValue(hint.FindingPlayer, out var findingSlot))
            {
                slotId = findingSlot.Id;
            }

            if (slotId is null)
            {
                continue;
            }

            // Items belong to the receiving player's game; locations to the
            // finding player's - the generic tracker login uses an empty
            // game, so the correct game is looked up per hinted player.
            var receivingGame = session.Players.GetPlayerInfo(hint.ReceivingPlayer)?.Game;
            var findingGame = session.Players.GetPlayerInfo(hint.FindingPlayer)?.Game;

            snapshots.Add(new HintSnapshot
            {
                Key = $"{hint.ReceivingPlayer}:{hint.FindingPlayer}:{hint.ItemId}:{hint.LocationId}",
                SlotId = slotId.Value,
                ReceivingPlayer = hint.ReceivingPlayer,
                FindingPlayer = hint.FindingPlayer,
                ReceivingPlayerName = session.Players.GetPlayerAlias(hint.ReceivingPlayer),
                FindingPlayerName = session.Players.GetPlayerAlias(hint.FindingPlayer),
                ItemName = session.Items.GetItemName(hint.ItemId, receivingGame) ?? $"Item #{hint.ItemId}",
                LocationName = session.Locations.GetLocationNameFromId(hint.LocationId, findingGame) ?? $"Location #{hint.LocationId}",
                Found = hint.Found,
                ItemFlags = hint.ItemFlags,
                ReceivingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.ReceivingPlayer, ownSlotNumeric, siblingIds),
                FindingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.FindingPlayer, ownSlotNumeric, siblingIds)
            });
        }

        if (snapshots.Count > 0)
        {
            _hintService.SyncHints(group, snapshots);
        }
    }

    /// <summary>
    /// Reads <paramref name="session"/>'s current "X of Y locations checked"
    /// counts onto <paramref name="slot"/>, dispatched to the UI thread
    /// since this can run from a session's background event thread.
    /// </summary>
    public static void UpdateLocationProgress(SlotProfile slot, IArchipelagoSession session)
    {
        var checkedCount = session.Locations.AllLocationsChecked.Count;
        var totalCount = session.Locations.AllLocations.Count;
        Dispatcher.UIThread.Post(() =>
        {
            slot.LocationsChecked = checkedCount;
            slot.LocationsTotal = totalCount;
        });
    }

    /// <summary>
    /// Refreshes the missing-locations cache for <paramref name="slot"/>.
    /// Not dispatched to the UI thread like <see cref="UpdateLocationProgress"/> -
    /// a plain dictionary entry is safe to write from any thread.
    /// </summary>
    public void UpdateMissingLocationsCache(SlotProfile slot, IArchipelagoSession session)
    {
        // Same roster-based game lookup as GetHintableItemsAsync -
        // ConnectionInfo.Game is never set for this generic tracker client.
        var ownGame = session.Players.GetPlayerInfo(session.ConnectionInfo.Slot)?.Game;

        IReadOnlyList<HintableLocation> missingLocations = session.Locations.AllMissingLocations
            .Select(id => new HintableLocation
            {
                LocationId = id,
                Name = session.Locations.GetLocationNameFromId(id, ownGame) ?? $"Location #{id}"
            })
            .ToList();

        _missingLocationsBySlot[slot.Id] = missingLocations;
    }
}
