using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="HintService"/>'s
/// IsNewSinceLastSession computation, and <see cref="GroupViewModel.VisibleHints"/>'s
/// filtering.
///
/// The <c>SyncHints</c>-based tests below need <c>[AvaloniaFact]</c>, not
/// plain <c>[Fact]</c>, despite <see cref="HintService.SyncHints"/> itself
/// never touching a Control - <see cref="Dispatcher.UIThread"/>'s job queue
/// can be drained manually via <see cref="Dispatcher.RunJobs"/> without a
/// running Avalonia Application (true before Kategorie C's
/// AvaloniaTestApplication assembly attribute existed - verified at the
/// time), but once that attribute is present, Dispatcher enforces real
/// thread ownership for the whole assembly, and a plain <c>[Fact]</c> runs on
/// a different thread than the one <c>[AvaloniaFact]</c> tests (and their
/// Dispatcher) get - see <c>VerifyAccess</c>'s
/// "calling thread cannot access this object" failure this produced. The
/// pure <c>VisibleHints</c> filter tests further down never touch
/// <see cref="Dispatcher"/> at all and correctly stay plain <c>[Fact]</c>.
/// </summary>
public class HintServiceFilterTests
{
    private static GroupViewModel MakeGroup(out SlotProfile slot)
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static HintSnapshot MakeSnapshot(
        string key,
        Guid slotId,
        bool found = false,
        EventTextSegmentKind findingKind = EventTextSegmentKind.OtherSlotName,
        EventTextSegmentKind receivingKind = EventTextSegmentKind.OtherSlotName) => new()
    {
        Key = key,
        SlotId = slotId,
        ReceivingPlayer = 1,
        FindingPlayer = 2,
        ReceivingPlayerName = "Bob",
        FindingPlayerName = "Alice",
        ItemName = "Sword",
        LocationName = "Chest",
        Found = found,
        ItemFlags = ItemFlags.None,
        ReceivingPlayerKind = receivingKind,
        FindingPlayerKind = findingKind,
    };

    // --- IsNewSinceLastSession (HintService.SyncHints) -----------------

    [AvaloniaFact]
    public void SyncHints_HintNotInSeenHintIds_IsMarkedNewSinceLastSession()
    {
        var groupViewModel = MakeGroup(out var slot);
        var syncStateStore = new InMemoryProfileSyncStateStore();
        var hintService = new HintService(syncStateStore);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("new-hint", slot.Id) });
        Dispatcher.UIThread.RunJobs();

        var entry = Assert.Single(groupViewModel.Hints);
        Assert.True(entry.IsNewSinceLastSession);
    }

    [AvaloniaFact]
    public void SyncHints_HintAlreadyInSeenHintIds_IsNotMarkedNew()
    {
        // Models a hint that was already shown in an earlier session (e.g.
        // before the app was closed and reopened).
        var groupViewModel = MakeGroup(out var slot);
        var syncStateStore = new InMemoryProfileSyncStateStore();
        syncStateStore.Seed(new ProfileSyncState { ProfileId = slot.Id, SeenHintIds = { "already-seen" } });
        var hintService = new HintService(syncStateStore);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("already-seen", slot.Id) });
        Dispatcher.UIThread.RunJobs();

        var entry = Assert.Single(groupViewModel.Hints);
        Assert.False(entry.IsNewSinceLastSession);
    }

    [AvaloniaFact]
    public void SyncHints_HintsThatArrivedWhileAway_AreMarkedNew_AndPersistedAsSeenAfterwards()
    {
        // "While away" = not in SeenHintIds yet, even though the batch also
        // contains an older, already-seen hint - each snapshot in the same
        // batch is judged independently against SeenHintIds.
        var groupViewModel = MakeGroup(out var slot);
        var syncStateStore = new InMemoryProfileSyncStateStore();
        syncStateStore.Seed(new ProfileSyncState { ProfileId = slot.Id, SeenHintIds = { "old-hint" } });
        var hintService = new HintService(syncStateStore);

        hintService.SyncHints(groupViewModel, new[]
        {
            MakeSnapshot("old-hint", slot.Id),
            MakeSnapshot("hint-that-arrived-while-away", slot.Id),
        });
        Dispatcher.UIThread.RunJobs();

        var oldEntry = groupViewModel.Hints.Single(h => h.Key == "old-hint");
        var newEntry = groupViewModel.Hints.Single(h => h.Key == "hint-that-arrived-while-away");
        Assert.False(oldEntry.IsNewSinceLastSession);
        Assert.True(newEntry.IsNewSinceLastSession);

        // Persisted so a later resync of the same hints doesn't call it "new" again.
        var persisted = syncStateStore.Get(slot.Id);
        Assert.Contains("hint-that-arrived-while-away", persisted.SeenHintIds);
    }

    [AvaloniaFact]
    public void SyncHints_ExistingHint_UpdatesFoundInPlace_WithoutChangingIsNewSinceLastSession()
    {
        var groupViewModel = MakeGroup(out var slot);
        var syncStateStore = new InMemoryProfileSyncStateStore();
        var hintService = new HintService(syncStateStore);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint-1", slot.Id, found: false) });
        Dispatcher.UIThread.RunJobs();
        var firstEntry = Assert.Single(groupViewModel.Hints);
        Assert.True(firstEntry.IsNewSinceLastSession);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint-1", slot.Id, found: true) });
        Dispatcher.UIThread.RunJobs();

        var updatedEntry = Assert.Single(groupViewModel.Hints);
        Assert.Same(firstEntry, updatedEntry); // same instance, updated in place
        Assert.True(updatedEntry.Found);
        Assert.True(updatedEntry.IsNewSinceLastSession); // init-only, unaffected by the Found update
    }

    // --- VisibleHints filtering (GroupViewModel) ------------------------

    [Fact]
    public void VisibleHints_UnfoundFilter_ExcludesFoundHints()
    {
        var groupViewModel = MakeGroup(out var slot);
        groupViewModel.Hints.Add(MakeEntry("unfound-hint", slot.Id, found: false));
        groupViewModel.Hints.Add(MakeEntry("found-hint", slot.Id, found: true));

        groupViewModel.SelectedHintFilter = HintFilter.Unfound;

        Assert.Equal(new[] { "unfound-hint" }, groupViewModel.VisibleHints.Select(h => h.Key));
    }

    [Fact]
    public void VisibleHints_AllFilter_IncludesFoundHints()
    {
        var groupViewModel = MakeGroup(out var slot);
        groupViewModel.Hints.Add(MakeEntry("unfound-hint", slot.Id, found: false));
        groupViewModel.Hints.Add(MakeEntry("found-hint", slot.Id, found: true));

        groupViewModel.SelectedHintFilter = HintFilter.All;

        Assert.Equal(
            new[] { "unfound-hint", "found-hint" },
            groupViewModel.VisibleHints.Select(h => h.Key));
    }

    [Fact]
    public void VisibleHints_RoleFilterIFind_OnlyShowsHintsWhereThisSlotIsTheFinder()
    {
        var groupViewModel = MakeGroup(out var slot);
        groupViewModel.SelectedHintFilter = HintFilter.All;
        groupViewModel.Hints.Add(MakeEntry("i-find-own", slot.Id,
            findingKind: EventTextSegmentKind.OwnSlotName, receivingKind: EventTextSegmentKind.OtherSlotName));
        groupViewModel.Hints.Add(MakeEntry("i-find-connected", slot.Id,
            findingKind: EventTextSegmentKind.ConnectedSlotName, receivingKind: EventTextSegmentKind.OtherSlotName));
        groupViewModel.Hints.Add(MakeEntry("i-receive-only", slot.Id,
            findingKind: EventTextSegmentKind.OtherSlotName, receivingKind: EventTextSegmentKind.OwnSlotName));

        groupViewModel.SelectedHintRoleFilter = HintRoleFilter.IFind;

        Assert.Equal(
            new[] { "i-find-connected", "i-find-own" },
            groupViewModel.VisibleHints.Select(h => h.Key).OrderBy(k => k));
    }

    [Fact]
    public void VisibleHints_RoleFilterIReceive_OnlyShowsHintsWhereThisSlotIsTheReceiver()
    {
        var groupViewModel = MakeGroup(out var slot);
        groupViewModel.SelectedHintFilter = HintFilter.All;
        groupViewModel.Hints.Add(MakeEntry("i-find-own", slot.Id,
            findingKind: EventTextSegmentKind.OwnSlotName, receivingKind: EventTextSegmentKind.OtherSlotName));
        groupViewModel.Hints.Add(MakeEntry("i-receive-own", slot.Id,
            findingKind: EventTextSegmentKind.OtherSlotName, receivingKind: EventTextSegmentKind.OwnSlotName));
        groupViewModel.Hints.Add(MakeEntry("i-receive-connected", slot.Id,
            findingKind: EventTextSegmentKind.OtherSlotName, receivingKind: EventTextSegmentKind.ConnectedSlotName));

        groupViewModel.SelectedHintRoleFilter = HintRoleFilter.IReceive;

        Assert.Equal(
            new[] { "i-receive-connected", "i-receive-own" },
            groupViewModel.VisibleHints.Select(h => h.Key).OrderBy(k => k));
    }

    private static HintEntry MakeEntry(
        string key,
        Guid slotId,
        bool found = false,
        EventTextSegmentKind findingKind = EventTextSegmentKind.OtherSlotName,
        EventTextSegmentKind receivingKind = EventTextSegmentKind.OtherSlotName) => new()
    {
        Key = key,
        SlotId = slotId,
        ReceivingPlayerName = "Bob",
        FindingPlayerName = "Alice",
        ItemName = "Sword",
        LocationName = "Chest",
        Found = found,
        FindingPlayerKind = findingKind,
        ReceivingPlayerKind = receivingKind,
    };
}
