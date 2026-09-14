using System;
using System.IO;
using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="MainWindowViewModel.ReorderGroup"/>
/// and <see cref="DashboardViewModel.ReorderGroup"/>'s own arithmetic/persistence -
/// the ViewModel-side half of manual tab-reordering (Feature-Plaene/Tab-Reihenfolge.md).
/// The actual Avalonia drag gesture (PointerPressed/Moved threshold detection,
/// DragDrop.DoDragDropAsync, and the DragOver-time before/after-half detection
/// that becomes <c>insertAfter</c> here) is deliberately not exercised - the
/// plan's own "Tests" section pre-approves calling the reorder method
/// directly instead, since Avalonia.Headless has no drag-gesture simulation
/// equivalent to its <c>Window.MouseDown</c>/<c>MouseUp</c> click helpers,
/// and Avalonia 12's <c>DoDragDropAsync</c> needs a registered
/// <c>IPlatformDragSource</c> that headless test runs don't provide - an
/// honestly documented gap, same as <see cref="ChatSlotComboBoxTests"/>'s
/// own caveat about its Loaded-handler fix.
/// </summary>
public sealed class GroupReorderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _persistenceService;

    public GroupReorderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        _persistenceService = new PersistenceService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Covers all four combinations of "source before/after target in the
    /// list" x "insertAfter true/false" - the index math in
    /// <see cref="MainWindowViewModel.ReorderGroup"/> treats these
    /// differently (see that method's own doc comment on why), and each was
    /// hand-verified against this exact scenario before being written.
    /// </summary>
    [Theory]
    [InlineData("Alpha", "Charlie", true, new[] { "Bravo", "Charlie", "Alpha" })] // forward drag, drop after target
    [InlineData("Alpha", "Charlie", false, new[] { "Bravo", "Alpha", "Charlie" })] // forward drag, drop before target
    [InlineData("Charlie", "Alpha", false, new[] { "Charlie", "Alpha", "Bravo" })] // backward drag, drop before target
    [InlineData("Charlie", "Alpha", true, new[] { "Alpha", "Charlie", "Bravo" })] // backward drag, drop after target
    public void ReorderGroup_PlacesSourceBeforeOrAfterTarget_AndPersistsTheNewOrder(
        string sourceName, string targetName, bool insertAfter, string[] expectedOrder)
    {
        var mainWindowViewModel = new MainWindowViewModel(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Charlie", "hostC", 3, string.Empty, "SlotC", autoConnect: false);
        var source = mainWindowViewModel.Groups.Single(g => g.Group.Name == sourceName);
        var target = mainWindowViewModel.Groups.Single(g => g.Group.Name == targetName);

        // Same ObservableCollection<T>.Move semantics as the real drop
        // handlers (MainWindow.axaml.cs's OnGroupTabDrop / DashboardView.axaml.cs's
        // OnOverviewRowDrop), just with insertAfter passed directly instead
        // of read off GroupViewModel.DropIndicator.
        mainWindowViewModel.ReorderGroup(source, target, insertAfter);

        Assert.Equal(expectedOrder, mainWindowViewModel.Groups.Select(g => g.Group.Name));

        // GetAllGroups() (and so groups.json) reads Groups' own order
        // directly - no separate "OrderIndex" property (see the plan) - so
        // reloading from disk must reflect the same new order.
        var reloaded = _persistenceService.LoadGroups();
        Assert.Equal(expectedOrder, reloaded.Select(g => g.Name));
    }

    [Fact]
    public void ReorderGroup_SameGroupAsSourceAndTarget_IsANoOp()
    {
        var mainWindowViewModel = new MainWindowViewModel(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];

        mainWindowViewModel.ReorderGroup(alpha, alpha, insertAfter: true);

        // Still just the AddNewGroup calls' own order - nothing moved.
        Assert.Equal(new[] { "Alpha", "Bravo" }, mainWindowViewModel.Groups.Select(g => g.Group.Name));
    }

    /// <summary>A group removed (e.g. "Remove server") mid-drag, before the drop lands - OnGroupTabDrop/OnOverviewRowDrop still call through to here with a now-stale source or target.</summary>
    [Fact]
    public void ReorderGroup_SourceOrTargetNoLongerInGroups_IsANoOp()
    {
        var mainWindowViewModel = new MainWindowViewModel(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        var bravo = mainWindowViewModel.Groups[1];

        mainWindowViewModel.Groups.Remove(bravo);

        mainWindowViewModel.ReorderGroup(alpha, bravo, insertAfter: false);

        Assert.Equal(new[] { "Alpha" }, mainWindowViewModel.Groups.Select(g => g.Group.Name));
    }

    /// <summary>
    /// <see cref="DashboardViewModel.ReorderGroup"/> is a pure delegation to
    /// whatever callback it was constructed with (same callback-based
    /// decoupling as <see cref="DashboardViewModel.SelectGroupAndLeaveDashboard"/>,
    /// see <see cref="DashboardViewModelTests"/> for that side's own
    /// coverage) - verified here end-to-end through the real
    /// <see cref="MainWindowViewModel"/> wiring instead of a bare callback,
    /// since that's what actually matters: dragging an Overview row really
    /// does reorder <see cref="MainWindowViewModel.Groups"/> and persist it.
    /// </summary>
    [Fact]
    public void DashboardViewModel_ReorderGroup_ReordersTheSameUnderlyingGroupsCollection()
    {
        var mainWindowViewModel = new MainWindowViewModel(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        var bravo = mainWindowViewModel.Groups[1];

        mainWindowViewModel.Dashboard.ReorderGroup(bravo, alpha, insertAfter: false);

        Assert.Equal(new[] { "Bravo", "Alpha" }, mainWindowViewModel.Groups.Select(g => g.Group.Name));
        Assert.Equal(new[] { "Bravo", "Alpha" }, _persistenceService.LoadGroups().Select(g => g.Name));
    }
}
