using System.Collections.Generic;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

public class HintService : IHintService
{
    private readonly IProfileSyncStateStore _syncStateStore;

    /// <summary>
    /// Optional - null in every existing test construction site that
    /// doesn't care about Feature-Plaene/Tab-Eigenes-Fenster.md's window
    /// flash, same "purely additive dependency" reasoning as this app's
    /// other optional services (e.g. <see cref="ViewModels.MainWindowViewModel"/>'s
    /// own <c>IUpdateService?</c>).
    /// </summary>
    private readonly IWindowAttentionService? _windowAttentionService;

    public HintService(IProfileSyncStateStore syncStateStore, IWindowAttentionService? windowAttentionService = null)
    {
        _syncStateStore = syncStateStore;
        _windowAttentionService = windowAttentionService;
    }

    public void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints)
    {
        // The whole diff runs on the UI thread: group.Hints is owned by the UI and
        // must not be enumerated from a background thread while a previous,
        // still-pending dispatched Add for the same group could be applied
        // concurrently (TrackHints can fire again in quick succession).
        Dispatcher.UIThread.Post(() =>
        {
            // A hint batch covers the whole room; different snapshots can
            // concern different configured slots, so the "already seen"
            // sync state is looked up per snapshot rather than once per call.
            var changedStates = new HashSet<ProfileSyncState>();
            var index = new HintIndex(group.Hints);

            foreach (var snapshot in hints)
            {
                AddOrUpdate(group, index, snapshot, addEventEntry: true, changedStates);
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

            // No event entry of its own: the chat line this came from is
            // already in the log (typed HintReceived, see
            // SessionEventTranslator.OnLeaderMessageReceived).
            AddOrUpdate(group, new HintIndex(group.Hints), snapshot, addEventEntry: false, changedStates);

            foreach (var state in changedStates)
            {
                _syncStateStore.Save(state);
            }
        });
    }

    /// <summary>
    /// The one place a hint enters <see cref="GroupViewModel.Hints"/>, whether
    /// it came from TrackHints or from a "[Hint]: ..." chat line. An existing
    /// entry for the same hint - same <see cref="HintEntry.Key"/>, or same
    /// finder + location (a location holds exactly one item, so that pair
    /// alone identifies a hint) - is only updated in place, never duplicated.
    /// Must run on the UI thread.
    /// </summary>
    private void AddOrUpdate(GroupViewModel group, HintIndex index, HintSnapshot snapshot, bool addEventEntry, HashSet<ProfileSyncState> changedStates)
    {
        var existing = index.Find(snapshot);

        if (existing is not null)
        {
            if (existing.Found != snapshot.Found)
            {
                // HintEntry is an ObservableObject, so this updates the UI in
                // place without needing to remove/re-add the item. The
                // VisibleHints filter on GroupViewModel will hide it automatically
                // when the hint filter is set to Unfound.
                existing.Found = snapshot.Found;
            }

            return;
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

            // Feature-Plaene/Tab-Eigenes-Fenster.md, Phase 2: a genuinely
            // new, still-unfound hint is exactly the condition already
            // guarding the event-log entry above - reuse it rather than
            // re-deriving "is this worth flashing for" separately. Applies
            // to a chat-sourced hint too, since whichever source arrives
            // first is the only one that ever gets this far.
            _windowAttentionService?.RequestAttention(group.Group.Id);
        }

        if (syncState.SeenHintIds.Add(snapshot.Key))
        {
            changedStates.Add(syncState);
        }
    }

    /// <summary>
    /// Lookup of a group's existing hints by <see cref="HintEntry.Key"/> and by
    /// finder + location - see <see cref="AddOrUpdate"/>. A TrackHints batch
    /// can carry the room's whole hint list, so this avoids a linear scan of
    /// <see cref="GroupViewModel.Hints"/> per snapshot.
    /// </summary>
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
