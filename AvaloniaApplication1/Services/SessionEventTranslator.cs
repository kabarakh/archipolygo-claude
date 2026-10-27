using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// Turns raw <see cref="IArchipelagoSession"/> events into <see cref="IMessageHistoryService"/>/<see cref="IHintService"/> calls.
/// See Umsetzungsplan.md, section "ConnectionManager: Locking, Nebenläufigkeit und Workarounds im Detail".
/// </summary>
internal sealed class SessionEventTranslator
{
    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;
    private readonly IAttentionTracker? _attentionTracker;

    // See Umsetzungsplan.md, section "_missingLocationsBySlot: bewusst nur im Speicher".
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<HintableLocation>> _missingLocationsBySlot = new();

    public SessionEventTranslator(IMessageHistoryService messageHistoryService, IHintService hintService, IAttentionTracker? attentionTracker = null)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
        _attentionTracker = attentionTracker;
    }

    /// <summary>See Umsetzungsplan.md, section "GetHintableLocationsAsync: Fast-Path und Re-Check".</summary>
    public bool TryGetMissingLocations(Guid slotId, out IReadOnlyList<HintableLocation> locations) =>
        _missingLocationsBySlot.TryGetValue(slotId, out locations!);

    /// <summary>
    /// Maps numeric Archipelago slot ids to configured slots; also refreshes <see cref="SlotProfile.Alias"/>.
    /// See Umsetzungsplan.md, section "BuildSlotRoster und Alias-Refresh".
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
    /// Adds a <c>TrackHints</c> subscription on the leader's session for a slot added after the leader connected.
    /// See Umsetzungsplan.md, section "Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht".
    /// </summary>
    public void TrackHintsForSiblingOnLeader(GroupViewModel group, IArchipelagoSession leaderSession, SlotProfile newSlot)
    {
        var roster = BuildSlotRoster(leaderSession, group.Group);
        foreach (var (numericSlotId, matchedSlot) in roster)
        {
            if (matchedSlot.Id == newSlot.Id)
            {
                // Leader is past its grace period, so only the first (replay) callback is backlog.
                var replayDelivered = false;
                leaderSession.Hints.TrackHints(
                    hints =>
                    {
                        OnHintsUpdated(group, leaderSession, hints, isLive: replayDelivered);
                        replayDelivered = true;
                    },
                    retrieveCurrentlyUnlockedHints: true,
                    slot: numericSlotId);
                return;
            }
        }
    }

    /// <summary>
    /// One status read per leader connect; afterwards <see cref="GoalLogMessage"/> keeps it current, since the
    /// server never undoes a goal.
    /// </summary>
    public async Task ReadGoalStatesAsync(GroupViewModel group, IArchipelagoSession session)
    {
        var players = ConnectionManager.FilterToRealPlayers(session.Players.AllPlayers);
        var statuses = await Task.WhenAll(players.Select(p => session.DataStorage.GetClientStatusAsync(p.Slot, p.Team)));

        var goaled = players.Where((_, i) => statuses[i] == ArchipelagoClientState.ClientGoal).Select(p => p.Slot).ToHashSet();
        goaled.UnionWith(GoaledItemLinkGroups(session, goaled));

        var seed = session.RoomState.Seed;
        Dispatcher.UIThread.Post(() => group.ApplyGoals(seed, goaled));
    }

    internal void OnGoalReached(GroupViewModel group, IArchipelagoSession session, int slotNumber)
    {
        var goaled = group.GoaledSlotNumbers.ToHashSet();
        goaled.Add(slotNumber);
        goaled.UnionWith(GoaledItemLinkGroups(session, goaled));

        Dispatcher.UIThread.Post(() => group.ApplyGoals(seed: null, goaled));
    }

    private static IEnumerable<int> GoaledItemLinkGroups(IArchipelagoSession session, IReadOnlySet<int> goaledSlots) =>
        // Not via FilterToRealPlayers: that drops exactly these item-link groups.
        session.Players.AllPlayers
            .Where(p => p.IsGroup && p.GetGroupMembers(session.Players).All(member => goaledSlots.Contains(member.Slot)))
            .Select(p => p.Slot);

    /// <summary>Item links are fixed per seed, so one read per leader connect is enough.</summary>
    public void ReadItemLinkGroups(GroupViewModel group, IArchipelagoSession session)
    {
        // Not via FilterToRealPlayers: that drops exactly these item-link groups.
        var snapshots = session.Players.AllPlayers
            .Where(p => p.IsGroup)
            .Select(p => new ItemLinkGroupSnapshot(
                ToRoomPlayerRef(p),
                p.Game,
                p.GetGroupMembers(session.Players).Select(ToRoomPlayerRef).ToList()))
            .ToList();

        Dispatcher.UIThread.Post(() => group.ApplyItemLinkGroups(snapshots));
    }

    private static RoomPlayerRef ToRoomPlayerRef(PlayerInfo player) =>
        new(player.Slot, player.Name, SlotProfile.FormatDisplayName(player.Name, player.Alias));

    private static HashSet<int> SiblingIdsExcludingOwn(Dictionary<int, SlotProfile> roster, int ownNumericSlotId) =>
        new(roster.Keys.Where(id => id != ownNumericSlotId));

    /// <summary>See Umsetzungsplan.md, section "ResolvePrimarySlotId".</summary>
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
    /// An untyped PrintJSON (MultiServer.py's broadcast_text_all) arrives as the exact <see cref="LogMessage"/> base type;
    /// Release/Collect notices have their own subtypes.
    /// </summary>
    internal static ServerReplyKind? ClassifyServerReply(LogMessage logMessage) => logMessage switch
    {
        AdminCommandResultLogMessage => ServerReplyKind.AdminCommandResult,
        CommandResultLogMessage => ServerReplyKind.CommandResult,
        ReleaseLogMessage or CollectLogMessage => ServerReplyKind.Broadcast,
        _ when logMessage.GetType() == typeof(LogMessage) => ServerReplyKind.Broadcast,
        _ => null,
    };

    private static bool IsAdminLine(LogMessage logMessage) => logMessage switch
    {
        AdminCommandResultLogMessage => AdminEventFormatter.IsAdminLine(AdminLineSource.AdminCommandResult, logMessage.ToString()),
        CommandResultLogMessage => AdminEventFormatter.IsAdminLine(AdminLineSource.CommandResult, logMessage.ToString()),
        ChatLogMessage chat => AdminEventFormatter.IsAdminLine(AdminLineSource.Chat, chat.Message),
        _ when logMessage.GetType() == typeof(LogMessage) => AdminEventFormatter.IsAdminLine(AdminLineSource.Broadcast, logMessage.ToString()),
        _ => false,
    };

    private static IReadOnlyList<(string Name, EventTextSegmentKind Kind)> BuildPlayerNameKinds(IArchipelagoSession session, IReadOnlySet<int> siblingIds)
    {
        var ownSlot = session.ConnectionInfo.Slot;
        var result = new List<(string, EventTextSegmentKind)>();
        foreach (var player in session.Players.AllPlayers.Where(p => p.Slot != 0 && !p.IsGroup))
        {
            var kind = EventSegmentBuilder.ClassifyPlayerSlot(player.Slot, ownSlot, siblingIds);
            result.Add((player.Name, kind));
            if (SlotProfile.StripRedundantSlotName(player.Name, player.Alias) is { Length: > 0 } alias)
            {
                result.Add((alias, kind));
            }
        }

        return result;
    }

    /// <summary>
    /// The leader sees the whole room's log, so item sends to non-leader slots are mirrored into their panels here
    /// without them needing a connection of their own.
    /// </summary>
    public void OnLeaderMessageReceived(GroupViewModel group, IArchipelagoSession session, SlotProfile leaderSlot, LogMessage logMessage)
    {
        var roster = BuildSlotRoster(session, group.Group);
        var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);

        var segments = EventSegmentBuilder.BuildChatSegments(logMessage, siblingIds, group.GetRoomSlotStatus);
        var receiver = logMessage is ItemSendLogMessage send ? group.GetRoomSlotStatus(send.Receiver.Slot) : null;
        var slotId = ResolvePrimarySlotId(logMessage, roster);
        // HintItemSendLogMessage derives from ItemSendLogMessage, so it must be matched first.
        var eventType = logMessage switch
        {
            HintItemSendLogMessage => EventType.HintReceived,
            ItemSendLogMessage => EventType.ItemReceived,
            _ => EventType.Chat
        };

        var isAdminLine = IsAdminLine(logMessage);
        var text = logMessage.ToString();
        if (isAdminLine)
        {
            eventType = EventType.Admin;
            text = AdminEventFormatter.MaskPasswords(text);
            segments = AdminEventFormatter.BuildSegments(text, BuildPlayerNameKinds(session, siblingIds));
        }

        _messageHistoryService.HandleChatMessage(group, text, segments, slotId, eventType, receiver);

        if (logMessage is GoalLogMessage goal)
        {
            OnGoalReached(group, session, goal.Player.Slot);
        }

        // Server lines, join/leave notices and "[Hint]: ..." lines are separate LogMessage types, so only
        // genuine player chat lands here; an "!admin ..." echo never deserves attention.
        if (logMessage is ChatLogMessage chat && !isAdminLine)
        {
            var ownNames = roster.Values.SelectMany(configured => new[] { configured.SlotName, configured.Alias });
            var category = ChatAttentionClassifier.Classify(roster.ContainsKey(chat.Player.Slot), chat.Message, ownNames);
            if (category is not null)
            {
                _attentionTracker?.Report(group, category.Value);
            }
        }

        if (logMessage is HintItemSendLogMessage hintMessage)
        {
            // A hint is not a received item - it only feeds the Hints panel.
            AddChatHint(group, session, roster, siblingIds, hintMessage);
            return;
        }

        if (logMessage is ItemSendLogMessage itemSend &&
            itemSend.Receiver.Slot != session.ConnectionInfo.Slot &&
            roster.TryGetValue(itemSend.Receiver.Slot, out var targetSlot))
        {
            var senderKind = EventSegmentBuilder.ClassifyPlayerSlot(itemSend.Item.Player, session.ConnectionInfo.Slot, siblingIds);
            var senderName = PlayerDisplayName(session, itemSend.Item.Player);
            _messageHistoryService.HandleObservedItemForSlot(
                group, targetSlot,
                itemSend.Item.ItemDisplayName, itemSend.Item.LocationDisplayName, itemSend.Item.Flags,
                senderName, senderKind, receiver);

            // Passive coverage has no backlog, so this is always live.
            if (receiver?.HasGoal != true)
            {
                _attentionTracker?.Report(group, ItemAttentionCategory(itemSend.Item.Flags));
            }
        }
    }

    /// <summary>
    /// Same key format as <see cref="OnHintsUpdated"/>, so whichever source reports a hint first wins; the server
    /// replays every hint on connect, so most of these are dropped as duplicates.
    /// </summary>
    private void AddChatHint(GroupViewModel group, IArchipelagoSession session, Dictionary<int, SlotProfile> roster,
                             HashSet<int> siblingIds, HintItemSendLogMessage hintMessage)
    {
        var receivingPlayer = hintMessage.Receiver.Slot;
        var findingPlayer = hintMessage.Item.Player.Slot;

        Guid? slotId = null;
        if (roster.TryGetValue(receivingPlayer, out var receivingSlot))
        {
            slotId = receivingSlot.Id;
        }
        else if (roster.TryGetValue(findingPlayer, out var findingSlot))
        {
            slotId = findingSlot.Id;
        }

        if (slotId is null)
        {
            return;
        }

        var ownSlotNumeric = session.ConnectionInfo.Slot;
        _hintService.AddHintFromChat(group, new HintSnapshot
        {
            Key = $"{receivingPlayer}:{findingPlayer}:{hintMessage.Item.ItemId}:{hintMessage.Item.LocationId}",
            SlotId = slotId.Value,
            ReceivingPlayer = receivingPlayer,
            FindingPlayer = findingPlayer,
            ReceivingPlayerName = PlayerDisplayName(session, receivingPlayer),
            FindingPlayerName = PlayerDisplayName(session, findingPlayer),
            ItemName = hintMessage.Item.ItemDisplayName,
            LocationName = hintMessage.Item.LocationDisplayName,
            Found = hintMessage.IsFound,
            ItemFlags = hintMessage.Item.Flags,
            ReceivingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(receivingPlayer, ownSlotNumeric, siblingIds),
            FindingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(findingPlayer, ownSlotNumeric, siblingIds)
        });
    }

    public void OnItemReceived(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, ReceivedItemsHelper helper, bool isLeaderSession, bool hasAnnouncedConnection)
    {
        // See Umsetzungsplan.md, section "Item-Backlog vs. Live bei OnItemReceived".
        var isLive = isLeaderSession && hasAnnouncedConnection;
        var receiver = group.GetRoomSlotStatus(session.ConnectionInfo.Slot);

        if (isLive)
        {
            // The chat-log "X sent Y to Z" line already announces it; just advance the persisted index.
            _messageHistoryService.AdvanceItemSyncState(slot, helper.AllItemsReceived);
        }
        else
        {
            _messageHistoryService.HandleItemsReceivedSinceLastConnection(group, slot, helper.AllItemsReceived, receiver);
        }

        var senderName = string.Empty;
        var senderKind = EventTextSegmentKind.OtherSlotName;
        if (helper.AllItemsReceived.Count > 0)
        {
            var latest = helper.AllItemsReceived[helper.AllItemsReceived.Count - 1];
            senderName = PlayerDisplayName(session, latest.Player);
            var roster = BuildSlotRoster(session, group.Group);
            var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);
            senderKind = EventSegmentBuilder.ClassifyPlayerSlot(latest.Player, session.ConnectionInfo.Slot, siblingIds);

            // Catch-up backlog must never trigger attention.
            if (isLive && !receiver.HasGoal)
            {
                _attentionTracker?.Report(group, ItemAttentionCategory(latest.Flags));
            }
        }

        _messageHistoryService.TrackReceivedItem(group, slot, helper.AllItemsReceived, senderName, senderKind);

        while (helper.Any())
        {
            helper.DequeueItem();
        }
    }

    /// <summary>
    /// Fires per tracked slot, not room-wide.
    /// See Umsetzungsplan.md, section "Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht".
    /// </summary>
    /// <param name="isLive">False for catch-up and the leader's initial replay: hints are added but never reported for attention.</param>
    public void OnHintsUpdated(GroupViewModel group, IArchipelagoSession session, Hint[] hints, bool isLive)
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

            // Items belong to the receiver's game, locations to the finder's; the tracker login has no game of its own.
            var receivingGame = session.Players.GetPlayerInfo(hint.ReceivingPlayer)?.Game;
            var findingGame = session.Players.GetPlayerInfo(hint.FindingPlayer)?.Game;

            snapshots.Add(new HintSnapshot
            {
                Key = $"{hint.ReceivingPlayer}:{hint.FindingPlayer}:{hint.ItemId}:{hint.LocationId}",
                SlotId = slotId.Value,
                ReceivingPlayer = hint.ReceivingPlayer,
                FindingPlayer = hint.FindingPlayer,
                ReceivingPlayerName = PlayerDisplayName(session, hint.ReceivingPlayer),
                FindingPlayerName = PlayerDisplayName(session, hint.FindingPlayer),
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
            _hintService.SyncHints(group, snapshots, isLive);
        }
    }

    private static string PlayerDisplayName(IArchipelagoSession session, int slot) =>
        session.Players.GetPlayerInfo(slot) is { } player
            ? SlotProfile.FormatDisplayName(player.Name, player.Alias)
            : string.Empty;

    private static AttentionCategory ItemAttentionCategory(Archipelago.MultiClient.Net.Enums.ItemFlags flags) =>
        EventSegmentBuilder.ClassifyItemFlags(flags) == EventTextSegmentKind.ItemProgression
            ? AttentionCategory.ProgressionItem
            : AttentionCategory.OtherItem;

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

    public void UpdateMissingLocationsCache(SlotProfile slot, IArchipelagoSession session)
    {
        // ConnectionInfo.Game is never set for this generic tracker client, so look the game up via the roster.
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
