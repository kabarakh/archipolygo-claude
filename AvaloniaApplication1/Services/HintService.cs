using System;
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

    public void SyncHints(TabViewModel tab, ServerProfile profile, IReadOnlyList<HintSnapshot> hints)
    {
        var syncState = _syncStateStore.Get(profile.Id);

        // The whole diff runs on the UI thread: tab.Hints is owned by the UI and
        // must not be enumerated from a background thread while a previous,
        // still-pending dispatched Add for the same tab could be applied
        // concurrently (TrackHints can fire again in quick succession).
        Dispatcher.UIThread.Post(() =>
        {
            var existingByKey = new Dictionary<string, HintEntry>();
            foreach (var entry in tab.Hints)
            {
                existingByKey[entry.Key] = entry;
            }

            var stateChanged = false;

            foreach (var snapshot in hints)
            {
                if (existingByKey.TryGetValue(snapshot.Key, out var existing))
                {
                    if (existing.Found != snapshot.Found)
                    {
                        // HintEntry is an ObservableObject, so this updates the UI in
                        // place without needing to remove/re-add the item.
                        existing.Found = snapshot.Found;
                    }

                    continue;
                }

                // First time we've seen this hint in this run. Whether it counts as
                // "new since last session" depends on whether it was already
                // persisted as seen the last time this profile was connected.
                var isNew = !syncState.SeenHintIds.Contains(snapshot.Key);

                var itemKind = EventSegmentBuilder.ClassifyItemFlags(snapshot.ItemFlags);

                var newEntry = new HintEntry
                {
                    Key = snapshot.Key,
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

                tab.Hints.Add(newEntry);
                tab.Events.Add(new EventEntry
                {
                    Type = EventType.HintReceived,
                    Text = $"Hint: {newEntry.ItemName} ({newEntry.FindingPlayerName} -> {newEntry.ReceivingPlayerName}, {newEntry.LocationName})",
                    Segments = EventSegmentBuilder.BuildHintReceivedSegments(
                        newEntry.ItemName, snapshot.ItemFlags,
                        newEntry.FindingPlayerName, snapshot.FindingPlayerKind,
                        newEntry.ReceivingPlayerName, snapshot.ReceivingPlayerKind,
                        newEntry.LocationName),
                    IsNewSinceLastSession = newEntry.IsNewSinceLastSession,
                    // Only a hint where this slot is the one receiving the item or
                    // the one who has to find it actually concerns it - TrackHints
                    // can also report hints between two other players.
                    ConcernsOwnSlot = snapshot.ReceivingPlayerKind == EventTextSegmentKind.OwnSlotName ||
                                      snapshot.FindingPlayerKind == EventTextSegmentKind.OwnSlotName
                });

                if (syncState.SeenHintIds.Add(snapshot.Key))
                {
                    stateChanged = true;
                }
            }

            if (stateChanged)
            {
                _syncStateStore.Save(syncState);
            }
        });
    }
}
