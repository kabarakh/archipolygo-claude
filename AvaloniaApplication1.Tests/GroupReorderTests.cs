using System;
using System.IO;
using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// The view-model half of tab reordering. The drag gesture itself isn't covered: headless has no drag simulation and
/// DoDragDropAsync needs an IPlatformDragSource headless runs don't provide.
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

    /// <summary>All four source-before/after-target x insertAfter combinations; the index math differs for each.</summary>
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

        // Same ObservableCollection.Move semantics as the real drop handlers, with insertAfter passed directly.
        mainWindowViewModel.ReorderGroup(source, target, insertAfter);

        Assert.Equal(expectedOrder, mainWindowViewModel.Groups.Select(g => g.Group.Name));

        // groups.json order is simply Groups' order (no OrderIndex property).
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

        Assert.Equal(new[] { "Alpha", "Bravo" }, mainWindowViewModel.Groups.Select(g => g.Group.Name));
    }

    /// <summary>A group removed mid-drag leaves the drop handler with a stale source or target.</summary>
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

    /// <summary>Verified through the real MainWindowViewModel wiring rather than a bare callback.</summary>
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
