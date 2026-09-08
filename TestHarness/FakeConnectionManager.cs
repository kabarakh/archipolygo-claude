using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace TestHarness;

/// <summary>
/// No-network stand-in for <see cref="IConnectionManager"/> - see
/// .claude/skills/app-testen. Never opens a real Archipelago session;
/// "connecting" a slot just flips the view model's own state so the UI reads
/// as connected. Test code adds synthetic <see cref="EventEntry"/>/
/// <see cref="HintEntry"/>/<see cref="ReceivedItemEntry"/> directly to a
/// GroupViewModel's collections rather than going through this class.
///
/// The one exception is hint routing (see <see cref="_hintsBySlot"/> and
/// <see cref="SimulateHintLocation"/> below): that logic in the real
/// <c>ConnectionManager</c> is subtle enough (a real bug shipped in 0.1.0,
/// see the ConnectionManager.cs history around TrackHints) that it's worth
/// modeling here too, so the "leader must also track every sibling slot's own
/// hints" behavior can be demonstrated and re-checked without a real
/// Archipelago server. It's a deliberately independent re-implementation of
/// the same design, not the production code itself - it can't catch a
/// regression in ConnectionManager.cs directly, only show whether the
/// *design* (mirrored here) still produces the right result.
/// </summary>
public sealed class FakeConnectionManager : IConnectionManager
{
    public event Action<GroupViewModel>? GroupPersistNeeded;
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    // Never raised: this fake never announces a sync batch before its
    // SlotInitialSyncCompleted calls (see InitializeGroupAsync below), so
    // MainWindowViewModel's StartupSyncTotal never grows and the "Catching
    // up slots: N/M" banner simply never shows here - fine for this fake's
    // purpose (see the class doc comment), just declared to satisfy
    // IConnectionManager.
    public event Action<int>? SlotSyncBatchStarting;

    // --- Fake hint room -----------------------------------------------
    //
    // Models the real Archipelago server's per-slot hints_{team}_{slot}
    // DataStorage keys (see MultiServer.py's notify_hints/on_new_hint): a
    // hint is stored under *both* the finding slot's and the receiving
    // slot's own key, and only clients that specifically subscribed to a
    // given key are notified when it changes - unlike chat/item-send lines,
    // there is no room-wide broadcast. _hintsBySlot/_hintSubscriptions are
    // keyed by a synthetic numeric "Archipelago slot id" (see
    // <see cref="NumericSlotId"/>), the same way the real code keys off
    // ArchipelagoSession's numeric ConnectionInfo.Slot.
    private readonly Dictionary<int, List<FakeHint>> _hintsBySlot = new();
    private readonly Dictionary<int, Action<List<FakeHint>>> _hintSubscriptions = new();
    private readonly Dictionary<Guid, int> _numericSlotIds = new();
    private int _nextNumericSlotId = 1;

    private int NumericSlotId(SlotProfile slot)
    {
        if (!_numericSlotIds.TryGetValue(slot.Id, out var id))
        {
            id = _nextNumericSlotId++;
            _numericSlotIds[slot.Id] = id;
        }

        return id;
    }

    public Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(targetSlot.Id, targetSlot);
        group.ConnectionState = ConnectionState.Connected;

        // Mirrors ConnectionManager.ConnectSlotSessionAsync's TrackHints
        // calls: one subscription for the leader's own slot (always
        // present), plus - the fix this models - one explicit subscription
        // per OTHER configured sibling slot in the same group, all over
        // this one "connection". Re-subscribing from scratch on every
        // leader switch keeps this in sync with whichever slot just became
        // leader, same as a real reconnect would.
        _hintSubscriptions.Clear();
        TrackHintsForSlot(group, targetSlot);
        foreach (var sibling in group.Group.Slots)
        {
            if (sibling.Id == targetSlot.Id)
            {
                continue;
            }

            TrackHintsForSlot(group, sibling);
        }

        GroupPersistNeeded?.Invoke(group);
        return Task.CompletedTask;
    }

    private void TrackHintsForSlot(GroupViewModel group, SlotProfile slot)
    {
        var numericId = NumericSlotId(slot);
        void OnUpdated(List<FakeHint> hints) => RouteHints(group, hints);

        _hintSubscriptions[numericId] = OnUpdated;

        // retrieveCurrentlyUnlockedHints: true - replay anything already
        // recorded under this key before the subscription existed.
        if (_hintsBySlot.TryGetValue(numericId, out var existing) && existing.Count > 0)
        {
            OnUpdated(existing);
        }
    }

    /// <summary>
    /// Stand-in for a player running "!hint_location" (or any other hint
    /// creation): <paramref name="finder"/> is the configured slot whose
    /// world the hinted location lives in; <paramref name="receiverName"/>
    /// is whoever owns the item there - which, as in the bug report this
    /// models, need not be one of this group's own configured slots at all.
    /// Bidirectional storage + per-key notification only (see
    /// <see cref="Notify"/>) means this reproduces the exact failure mode:
    /// if nothing is subscribed to <paramref name="finder"/>'s own key (the
    /// pre-fix behavior when <paramref name="finder"/> isn't the leader),
    /// the hint is created but never routed anywhere - silently lost, same
    /// as it was in the real app before the ConnectionManager.cs fix.
    /// </summary>
    public void SimulateHintLocation(GroupViewModel group, SlotProfile finder, string receiverName, string itemName, string locationName, ItemFlags flags)
    {
        var finderNumericId = NumericSlotId(finder);
        var configuredReceiver = group.Group.Slots.FirstOrDefault(s => string.Equals(s.SlotName, receiverName, StringComparison.OrdinalIgnoreCase));
        var receiverNumericId = configuredReceiver is not null
            ? NumericSlotId(configuredReceiver)
            : 100_000 + Math.Abs(receiverName.GetHashCode() % 1000); // unconfigured room player - never collides with a real configured slot's small id

        var hint = new FakeHint(finder, receiverName, finderNumericId, receiverNumericId, itemName, locationName, flags);

        Notify(finderNumericId, hint);
        if (receiverNumericId != finderNumericId)
        {
            Notify(receiverNumericId, hint);
        }
    }

    private void Notify(int numericSlotId, FakeHint hint)
    {
        if (!_hintsBySlot.TryGetValue(numericSlotId, out var list))
        {
            list = new List<FakeHint>();
            _hintsBySlot[numericSlotId] = list;
        }

        list.Add(hint);

        // If nobody currently tracks this key, the hint just sits in
        // _hintsBySlot unreported - exactly the pre-fix bug for a
        // non-leader sibling slot's own hints.
        if (_hintSubscriptions.TryGetValue(numericSlotId, out var callback))
        {
            callback(list);
        }
    }

    private void RouteHints(GroupViewModel group, IReadOnlyList<FakeHint> hints)
    {
        foreach (var hint in hints)
        {
            var key = $"{hint.FinderSlot.Id}:{hint.ReceiverName}:{hint.ItemName}:{hint.LocationName}";
            if (group.Hints.Any(h => h.Key == key))
            {
                continue; // already routed by an earlier notification
            }

            var finderKind = group.LeaderSlotId == hint.FinderSlot.Id
                ? EventTextSegmentKind.OwnSlotName
                : EventTextSegmentKind.ConnectedSlotName;

            group.Hints.Add(new HintEntry
            {
                Key = key,
                SlotId = hint.FinderSlot.Id,
                ReceivingPlayer = hint.ReceiverNumericId,
                FindingPlayer = hint.FinderNumericId,
                ReceivingPlayerName = hint.ReceiverName,
                FindingPlayerName = hint.FinderSlot.SlotName,
                ItemName = hint.ItemName,
                LocationName = hint.LocationName,
                ItemFlags = hint.ItemFlags,
                ItemKind = EventSegmentBuilder.ClassifyItemFlags(hint.ItemFlags),
                ReceivingPlayerKind = EventTextSegmentKind.OtherSlotName,
                FindingPlayerKind = finderKind,
                Found = false,
            });

            group.Events.Add(new EventEntry
            {
                SlotId = hint.FinderSlot.Id,
                Type = EventType.HintReceived,
                Text = $"Hint: {hint.ItemName} ({hint.FinderSlot.SlotName} -> {hint.ReceiverName}, {hint.LocationName})",
                Segments = EventSegmentBuilder.BuildHintReceivedSegments(
                    hint.ItemName, hint.ItemFlags,
                    hint.FinderSlot.SlotName, finderKind,
                    hint.ReceiverName, EventTextSegmentKind.OtherSlotName,
                    hint.LocationName),
                ConcernsOwnSlot = true,
            });
        }
    }

    private sealed record FakeHint(SlotProfile FinderSlot, string ReceiverName, int FinderNumericId, int ReceiverNumericId, string ItemName, string LocationName, ItemFlags ItemFlags);

    public Task DisconnectGroupAsync(GroupViewModel group)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(null, null);
        group.ConnectionState = ConnectionState.Disconnected;
        GroupPersistNeeded?.Invoke(group);
        return Task.CompletedTask;
    }

    public Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot) => Task.CompletedTask;

    public Task SendMessageAsync(GroupViewModel group, string text) => Task.CompletedTask;

    public Task InitializeGroupAsync(GroupViewModel group)
    {
        // Immediately mark every configured slot as "processed" so
        // MainWindowViewModel's startup-sync banner doesn't get stuck at
        // "Catching up slots: 0/N" forever - nothing here ever fires it.
        foreach (var slot in group.Group.Slots.ToList())
        {
            SlotInitialSyncCompleted?.Invoke(group, slot);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group) =>
        Task.FromResult<IReadOnlyList<PlayerInfo>>(Array.Empty<PlayerInfo>());
}
