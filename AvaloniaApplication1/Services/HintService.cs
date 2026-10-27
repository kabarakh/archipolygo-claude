using System.Collections.Generic;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

public class HintService : IHintService
{
    private readonly IProfileSyncStateStore _syncStateStore;

    private readonly IAttentionTracker? _attentionTracker;

    public HintService(IProfileSyncStateStore syncStateStore, IAttentionTracker? attentionTracker = null)
    {
        _syncStateStore = syncStateStore;
        _attentionTracker = attentionTracker;
    }

    public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints, bool isLive)
    {
        // Diff on the UI thread: TrackHints can fire in quick succession while a previous Add is still pending.
        Dispatcher.UIThread.Post(() =>
        {
            var changedStates = new HashSet<ProfileSyncState>();
            var index = new HintIndex(group.Hints);

            foreach (var snapshot in hints)
            {
                AddOrUpdate(group, index, snapshot, addEventEntry: true, reportAttention: isLive, changedStates);
            }

            foreach (var state in changedStates)
            {
                _syncStateStore.Save(state);
            }
        });
    }

    public void AddHintFromChat(GroupViewModel group, HintSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var changedStates = new HashSet<ProfileSyncState>();

            // The chat line itself is already in the log; chat lines are never replayed backlog, so always live.
            AddOrUpdate(group, new HintIndex(group.Hints), snapshot, addEventEntry: false, reportAttention: true, changedStates);

            foreach (var state in changedStates)
            {
                _syncStateStore.Save(state);
            }
        });
    }

    /// <summary>
    /// Dedups by key or by finder + location (a location holds exactly one item). Must run on the UI thread.
    /// </summary>
    private void AddOrUpdate(GroupViewModel group, HintIndex index, HintSnapshot snapshot, bool addEventEntry, bool reportAttention, HashSet<ProfileSyncState> changedStates)
    {
        var existing = index.Find(snapshot);

        if (existing is not null)
        {
            if (existing.Found != snapshot.Found)
            {
                existing.Found = snapshot.Found;
            }

            return;
        }

        var syncState = _syncStateStore.Get(snapshot.SlotId);

        // Found hints are still added (filtered at display time) but get no event entry: nothing left to act on.
        var isNew = !syncState.SeenHintIds.Contains(snapshot.Key);

        var itemKind = EventSegmentBuilder.ClassifyItemFlags(snapshot.ItemFlags);

        var newEntry = new HintEntry
        {
            Key = snapshot.Key,
            SlotId = snapshot.SlotId,
            ReceivingPlayer = snapshot.ReceivingPlayer,
            FindingPlayer = snapshot.FindingPlayer,
            ReceivingPlayerName = snapshot.ReceivingPlayerName,
            FindingPlayerName = snapshot.FindingPlayerName,
            ReceivingSlot = group.GetRoomSlotStatus(snapshot.ReceivingPlayer),
            FindingSlot = group.GetRoomSlotStatus(snapshot.FindingPlayer),
            ItemName = snapshot.ItemName,
            LocationName = snapshot.LocationName,
            Found = snapshot.Found,
            IsNewSinceLastSession = isNew,
            ItemFlags = snapshot.ItemFlags,
            ItemKind = itemKind,
            ReceivingPlayerKind = snapshot.ReceivingPlayerKind,
            FindingPlayerKind = snapshot.FindingPlayerKind
        };

        group.Hints.Add(newEntry);
        index.Add(newEntry);

        if (!snapshot.Found)
        {
            if (addEventEntry)
            {
                group.Events.Add(new EventEntry
                {
                    SlotId = snapshot.SlotId,
                    Type = EventType.HintReceived,
                    Text = $"Hint: {newEntry.ItemName} ({newEntry.FindingPlayerName} -> {newEntry.ReceivingPlayerName}, {newEntry.LocationName})",
                    Segments = EventSegmentBuilder.BuildHintReceivedSegments(
                        newEntry.ItemName, snapshot.ItemFlags,
                        newEntry.FindingPlayerName, snapshot.FindingPlayerKind,
                        newEntry.ReceivingPlayerName, snapshot.ReceivingPlayerKind,
                        newEntry.LocationName,
                        newEntry.FindingSlot, newEntry.ReceivingSlot),
                    Receiver = newEntry.ReceivingSlot,
                    IsNewSinceLastSession = newEntry.IsNewSinceLastSession,
                    // TrackHints also reports hints between unrelated players.
                    ConcernsOwnSlot = snapshot.ReceivingPlayerKind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName ||
                                      snapshot.FindingPlayerKind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName
                });
            }

            if (reportAttention && newEntry.ReceivingSlot?.HasGoal != true)
            {
                _attentionTracker?.Report(group, AttentionCategory.OwnHint);
            }
        }

        if (syncState.SeenHintIds.Add(snapshot.Key))
        {
            changedStates.Add(syncState);
        }
    }

    /// <summary>Avoids a linear scan per snapshot, since a TrackHints batch can carry the whole room's hints.</summary>
    private sealed class HintIndex
    {
        private readonly Dictionary<string, HintEntry> _byKey = new();
        private readonly Dictionary<(int FindingPlayer, string LocationName), HintEntry> _byFinderAndLocation = new();

        public HintIndex(IEnumerable<HintEntry> hints)
        {
            foreach (var hint in hints)
            {
                Add(hint);
            }
        }

        public void Add(HintEntry hint)
        {
            _byKey[hint.Key] = hint;
            _byFinderAndLocation[(hint.FindingPlayer, hint.LocationName)] = hint;
        }

        public HintEntry? Find(HintSnapshot snapshot) =>
            _byKey.TryGetValue(snapshot.Key, out var byKey) ? byKey
            : _byFinderAndLocation.TryGetValue((snapshot.FindingPlayer, snapshot.LocationName), out var byPair) ? byPair
            : null;
    }
}
