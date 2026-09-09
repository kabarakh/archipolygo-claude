using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): Tier 1 of Feature-Plaene/Fortschrittsanzeigen.md -
/// <see cref="GroupViewModel.RoomChecksCompleted"/>/<see cref="GroupViewModel.RoomChecksTotal"/>/
/// <see cref="GroupViewModel.HasRoomProgress"/>'s aggregation arithmetic over a
/// mix of synced/never-synced <see cref="SlotProfile"/>s, and their reactivity
/// to slot property changes and slot add/remove - no session/Avalonia
/// involved, same as <see cref="GroupViewModelOrderingTests"/>.
/// </summary>
public class GroupViewModelRoomProgressTests
{
    private static (GroupViewModel viewModel, ServerConnectionGroup group) MakeGroup(params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        var viewModel = new GroupViewModel(group, new FakeConnectionManager());

        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return (viewModel, group);
    }

    [Fact]
    public void NoSlotEverSynced_HasNoProgress()
    {
        var (viewModel, _) = MakeGroup("Alice", "Bob");

        Assert.False(viewModel.HasRoomProgress);
        Assert.Equal(0, viewModel.RoomChecksCompleted);
        Assert.Equal(0, viewModel.RoomChecksTotal);
        Assert.Equal(string.Empty, viewModel.RoomProgressText);
    }

    [Fact]
    public void SumsOnlySlotsThatHaveSynced_IgnoresNeverConnectedSlots()
    {
        var (viewModel, group) = MakeGroup("Alice", "Bob", "Carol");
        var alice = group.Slots.Single(s => s.SlotName == "Alice");
        var bob = group.Slots.Single(s => s.SlotName == "Bob");
        // Carol never connects - LocationsChecked/Total stay null.

        alice.LocationsChecked = 10;
        alice.LocationsTotal = 50;
        bob.LocationsChecked = 25;
        bob.LocationsTotal = 40;

        Assert.True(viewModel.HasRoomProgress);
        Assert.Equal(35, viewModel.RoomChecksCompleted);
        Assert.Equal(90, viewModel.RoomChecksTotal);
        Assert.Equal("35/90", viewModel.RoomProgressText);
    }

    [Fact]
    public void UpdatingASlotsProgress_AfterConstruction_IsReflectedImmediately()
    {
        var (viewModel, group) = MakeGroup("Alice");
        var alice = group.Slots.Single();

        alice.LocationsChecked = 1;
        alice.LocationsTotal = 10;
        Assert.Equal(1, viewModel.RoomChecksCompleted);

        alice.LocationsChecked = 5;
        Assert.Equal(5, viewModel.RoomChecksCompleted);
    }

    [Fact]
    public void RemovingASyncedSlot_ExcludesItFromTheAggregate()
    {
        var (viewModel, group) = MakeGroup("Alice", "Bob");
        var alice = group.Slots.Single(s => s.SlotName == "Alice");
        var bob = group.Slots.Single(s => s.SlotName == "Bob");
        alice.LocationsChecked = 10;
        alice.LocationsTotal = 20;
        bob.LocationsChecked = 5;
        bob.LocationsTotal = 5;
        Assert.Equal(15, viewModel.RoomChecksCompleted);

        viewModel.RemoveSlotFromGroup(bob);

        Assert.Equal(10, viewModel.RoomChecksCompleted);
        Assert.Equal(20, viewModel.RoomChecksTotal);

        // A slot removed from the configuration must stop being able to move
        // the aggregate too - not just be excluded at the moment of removal.
        bob.LocationsChecked = 999;
        Assert.Equal(10, viewModel.RoomChecksCompleted);
    }

    [Fact]
    public void AddingASlotViaAddSlotsToGroup_ParticipatesInTheAggregateOnceSynced()
    {
        var (viewModel, group) = MakeGroup("Alice");
        var alice = group.Slots.Single();
        alice.LocationsChecked = 10;
        alice.LocationsTotal = 20;

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        viewModel.AddSlotsToGroup(new[] { bob });
        Assert.Equal(10, viewModel.RoomChecksCompleted); // unchanged until Bob syncs

        bob.LocationsChecked = 3;
        bob.LocationsTotal = 9;

        Assert.Equal(13, viewModel.RoomChecksCompleted);
        Assert.Equal(29, viewModel.RoomChecksTotal);
    }

    /// <summary>
    /// The one combined progress bar's aggregation (see GroupViewModel.OwnChecksDone
    /// and friends) - "own" comes from Tier 1's own numbers, "other" is the
    /// tracker's whole-room numbers minus "own", never a per-player join.
    /// Same "no Avalonia needed" reasoning as the rest of this file -
    /// MultiworldProgress is populated directly.
    /// </summary>
    [Fact]
    public void CombinedProgress_OwnComesFromTier1_OtherIsWholeRoomMinusOwn()
    {
        var (viewModel, group) = MakeGroup("Alice");
        var alice = group.Slots.Single();
        alice.LocationsChecked = 30;
        alice.LocationsTotal = 100;
        group.TrackerId = "tracker-xyz";

        viewModel.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 1, ChecksDone = 30, ChecksTotal = 100 });
        viewModel.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 2, ChecksDone = 50, ChecksTotal = 200 });

        Assert.True(viewModel.HasAnyProgress);
        Assert.Equal(30, viewModel.OwnChecksDone);
        Assert.Equal(100, viewModel.OwnChecksTotal);
        Assert.Equal(70, viewModel.OwnChecksOpen);
        Assert.Equal(50, viewModel.OtherChecksDone);
        Assert.Equal(200, viewModel.OtherChecksTotal);
        Assert.Equal(150, viewModel.OtherChecksOpen);

        // Percentages are each segment's share of the whole bar (grand total
        // 300), not of its own done+open sub-category.
        Assert.Equal("Own, done: 30 (10%)", viewModel.OwnChecksDoneLegendText);
        Assert.Equal("Own, open: 70 (23%)", viewModel.OwnChecksOpenLegendText);
        Assert.Equal("Others, done: 50 (17%)", viewModel.OtherChecksDoneLegendText);
        Assert.Equal("Others, open: 150 (50%)", viewModel.OtherChecksOpenLegendText);
    }

    [Fact]
    public void CombinedProgress_TrackerStalerThanTier1_ClampsOtherToZeroRatherThanGoingNegative()
    {
        var (viewModel, group) = MakeGroup("Alice");
        var alice = group.Slots.Single();
        // Tier 1 (live) already reflects a check the tracker (polled, up to
        // 60s/300s stale) hasn't caught up to yet.
        alice.LocationsChecked = 50;
        alice.LocationsTotal = 100;
        group.TrackerId = "tracker-xyz";

        viewModel.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 1, ChecksDone = 40, ChecksTotal = 100 });

        Assert.Equal(0, viewModel.OtherChecksDone);
        Assert.Equal(0, viewModel.OtherChecksTotal);
        Assert.Equal(0, viewModel.OtherChecksOpen);
    }

    /// <summary>
    /// The fallback case: no tracker configured at all for this group. The
    /// combined bar still shows - just with its own two segments only, the
    /// "other" ones collapsing to zero share of the bar automatically
    /// (nothing bumps GrandTotalChecks above OwnChecksTotal). Not a separate
    /// UI element - see MainWindow.axaml's doc comment on the bar itself.
    /// </summary>
    [Fact]
    public void CombinedProgress_NoTrackerConfigured_StillHasProgress_JustOwnSegmentsOnly()
    {
        var (viewModel, group) = MakeGroup("Alice");
        var alice = group.Slots.Single();
        alice.LocationsChecked = 10;
        alice.LocationsTotal = 20;
        // group.TrackerId left null - Tier 2 not configured for this group.

        Assert.True(viewModel.HasAnyProgress);
        Assert.Equal(10, viewModel.OwnChecksDone);
        Assert.Equal(20, viewModel.OwnChecksTotal);
        Assert.Equal(0, viewModel.OtherChecksDone);
        Assert.Equal(0, viewModel.OtherChecksTotal);
        Assert.False(viewModel.HasMultiworldTracker); // drives hiding the "Others" legend rows and showing the "add a tracker" hint instead
    }

    [Fact]
    public void CombinedProgress_TrackerConfiguredButNothingFetchedYet_HasNoProgressAtAll()
    {
        var (viewModel, group) = MakeGroup("Alice");
        group.TrackerId = "tracker-xyz";
        // No Tier 1 sync, no MultiworldProgress entries yet.

        Assert.False(viewModel.HasAnyProgress);
    }
}
