using System.Collections.Generic;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

public class HintService : IHintService
{
    private readonly IProfileSyncStateStore _syncStateStore;

    public HintService(IProfileSyncStateStore syncStateStore)
    {
        _syncStateStore = syncStateStore;
    }

    public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints)
    {
        // The whole diff runs on the UI thread: group.Hints is owned by the UI and
        // must not be enumerated from a background thread while a previous,
        // still-pending dispatched Add for the same group could be applied
        // concurrently (TrackHints can fire again in quick succession).
        Dispatcher.UIThread.Post(() =>
        {
            var existingByKey = new Dictionary<string, HintEntry>();
            foreach (var entry in group.Hints)
            {
                existingByKey[entry.Key] = entry;
            }

            // A hint batch covers the whole room; different snapshots can
            // concern different configured slots, so the "already seen"
            // sync state is looked up per snapshot rather than once per call.
            var changedStates = new HashSet<ProfileSyncState>();

            foreach (var snapshot in hints)
            {
                if (existingByKey.TryGetValue(snapshot.Key, out var existing))
                {
                    if (existing.Found != snapshot.Found)
                    {
                        // HintEntry is an ObservableObject, so this updates the UI in
                        // place without needing to remove/re-add the item. The
                        // VisibleHints filter on GroupViewModel will hide it automatically
                        // when the hint filter is set to Unfound.
                        existing.Found = snapshot.Found;
                    }

                    continue;
                }

                var syncState = _syncStateStore.Get(snapshot.SlotId);

                // First time we've seen this hint. Always add it to group.Hints so
                // the "show found" button can reveal it - VisibleHints filters by
                // Found at display time. Only generate an event-log entry for
                // unfound hints though: already-found hints at login time are
                // historical noise with nothing left to act on.
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

                if (!snapshot.Found)
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
                            newEntry.LocationName),
                        IsNewSinceLastSession = newEntry.IsNewSinceLastSession,
                        // Only a hint where one of this group's configured slots is
                        // the one receiving the item or the one who has to find it
                        // actually concerns "me" - TrackHints reports every hint in
                        // the room, including ones between two unrelated players.
                        ConcernsOwnSlot = snapshot.ReceivingPlayerKind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName ||
                                          snapshot.FindingPlayerKind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName
                    });
                }

                if (syncState.SeenHintIds.Add(snapshot.Key))
                {
                    changedStates.Add(syncState);
                }
            }

            foreach (var state in changedStates)
            {
                _syncStateStore.Save(state);
            }
        });
    }
}
