using System;
using System.IO;
using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="MainWindowViewModel.DetachGroup"/>/
/// <see cref="MainWindowViewModel.RedockGroup"/>'s own bookkeeping - the
/// ViewModel-side half of Feature-Plaene/Tab-Eigenes-Fenster.md. Same
/// documented gap as <see cref="GroupReorderTests"/>: the actual drag
/// gesture that calls these (GroupReorderDragDrop, MainWindow.axaml.cs's
/// OnMainAreaDrop/OnGroupTabDrop, DetachedGroupWindow's own drag handle)
/// isn't exercised here - Avalonia.Headless has no drag-gesture simulation,
/// see that class's own doc comment for why.
/// </summary>
public sealed class TabDetachTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _persistenceService;

    public TabDetachTests()
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

    private MainWindowViewModel CreateViewModel() =>
        new(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());

    [Fact]
    public void DetachGroup_RemovesFromDockedGroups_ButKeepsItInGroups_AndMarksItDetached()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];

        mainWindowViewModel.DetachGroup(alpha);

        Assert.Contains(alpha, mainWindowViewModel.Groups);
        Assert.DoesNotContain(alpha, mainWindowViewModel.DockedGroups);
        Assert.True(alpha.IsDetached);
    }

    [Fact]
    public void DetachGroup_InvokesOpenDetachedWindow_WithTheDetachedGroup()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        GroupViewModel? opened = null;
        mainWindowViewModel.OpenDetachedWindow = g => opened = g;

        mainWindowViewModel.DetachGroup(alpha);

        Assert.Same(alpha, opened);
    }

    [Fact]
    public void DetachGroup_AlreadyDetached_IsANoOp()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.DetachGroup(alpha);
        var openCount = 0;
        mainWindowViewModel.OpenDetachedWindow = _ => openCount++;

        mainWindowViewModel.DetachGroup(alpha);

        Assert.Equal(0, openCount);
    }

    /// <summary>Detaching the currently selected (active) tab must move the selection to another still-docked tab rather than leaving it pointing at a group the TabControl no longer renders.</summary>
    [Fact]
    public void DetachGroup_DetachingTheSelectedGroup_ReassignsSelectedGroupToAnotherDockedGroup()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        var bravo = mainWindowViewModel.Groups[1];
        mainWindowViewModel.SelectedGroup = alpha;

        mainWindowViewModel.DetachGroup(alpha);

        Assert.Same(bravo, mainWindowViewModel.SelectedGroup);
    }

    /// <summary>The last remaining docked group's own tab being detached leaves nothing left to select.</summary>
    [Fact]
    public void DetachGroup_DetachingTheOnlyDockedGroup_LeavesSelectedGroupNull()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.SelectedGroup = alpha;

        mainWindowViewModel.DetachGroup(alpha);

        Assert.Null(mainWindowViewModel.SelectedGroup);
    }

    /// <summary>Detaching the very last docked tab would otherwise leave the TabControl empty - switch to the Dashboard automatically instead of a blank pane.</summary>
    [Fact]
    public void DetachGroup_DetachingTheOnlyDockedGroup_SwitchesToDashboard()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.IsDashboardVisible = false; // simulate the user currently viewing Tab View

        mainWindowViewModel.DetachGroup(alpha);

        Assert.True(mainWindowViewModel.IsDashboardVisible);
    }

    /// <summary>Detaching one of several still-docked tabs leaves a perfectly usable TabControl behind - no reason to force a view switch.</summary>
    [Fact]
    public void DetachGroup_DetachingOneOfSeveralDockedGroups_DoesNotSwitchToDashboard()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.IsDashboardVisible = false;

        mainWindowViewModel.DetachGroup(alpha);

        Assert.False(mainWindowViewModel.IsDashboardVisible);
    }

    [Fact]
    public void RedockGroup_AddsBackToDockedGroups_ClearsIsDetached_AndSelectsIt()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.DetachGroup(alpha);
        var closedWindows = 0;
        mainWindowViewModel.CloseDetachedWindow = _ => closedWindows++;

        mainWindowViewModel.RedockGroup(alpha);

        Assert.Contains(alpha, mainWindowViewModel.DockedGroups);
        Assert.False(alpha.IsDetached);
        Assert.Same(alpha, mainWindowViewModel.SelectedGroup);
        Assert.Equal(1, closedWindows);
    }

    [Fact]
    public void RedockGroup_NotCurrentlyDetached_IsANoOp()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        var closedWindows = 0;
        mainWindowViewModel.CloseDetachedWindow = _ => closedWindows++;

        mainWindowViewModel.RedockGroup(alpha);

        Assert.Equal(0, closedWindows);
        Assert.Single(mainWindowViewModel.DockedGroups);
    }

    /// <summary>Mirrors DetachGroup's own auto-switch: re-docking the only tab (nothing else was docked) switches back to Tab View so the user actually sees it land.</summary>
    [Fact]
    public void RedockGroup_WhenNothingElseWasDocked_SwitchesBackToTabView()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.DetachGroup(alpha); // auto-switches to Dashboard, see that test

        mainWindowViewModel.RedockGroup(alpha);

        Assert.False(mainWindowViewModel.IsDashboardVisible);
    }

    /// <summary>A user deliberately viewing the Dashboard while another tab stays docked shouldn't be yanked away from it just because an unrelated detached window closed.</summary>
    [Fact]
    public void RedockGroup_WhenAnotherGroupWasAlreadyDocked_DoesNotForceSwitchToTabView()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.DetachGroup(alpha); // Bravo stays docked, no auto-switch (see that test)
        mainWindowViewModel.IsDashboardVisible = true; // the user's own deliberate choice

        mainWindowViewModel.RedockGroup(alpha);

        Assert.True(mainWindowViewModel.IsDashboardVisible);
    }

    /// <summary>Removing a server entirely while its tab is detached must also close that orphaned window, not just drop it from Groups.</summary>
    [Fact]
    public async System.Threading.Tasks.Task RemoveGroupAsync_WhileDetached_ClosesTheDetachedWindowToo()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        mainWindowViewModel.DetachGroup(alpha);
        var closedWindows = 0;
        mainWindowViewModel.CloseDetachedWindow = _ => closedWindows++;

        await mainWindowViewModel.RemoveGroupAsync(alpha);

        Assert.DoesNotContain(alpha, mainWindowViewModel.Groups);
        Assert.Equal(1, closedWindows);
    }

    /// <summary>DockedGroups must follow the exact same Move as Groups, so the TabControl visually reflects a tab-header drag - see MainWindowViewModel.ReorderGroup's own doc comment.</summary>
    [Fact]
    public void ReorderGroup_AlsoReordersDockedGroups_InLockstepWithGroups()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Charlie", "hostC", 3, string.Empty, "SlotC", autoConnect: false);
        var alpha = mainWindowViewModel.Groups.Single(g => g.Group.Name == "Alpha");
        var charlie = mainWindowViewModel.Groups.Single(g => g.Group.Name == "Charlie");

        mainWindowViewModel.ReorderGroup(alpha, charlie, insertAfter: true);

        Assert.Equal(
            mainWindowViewModel.Groups.Select(g => g.Group.Name),
            mainWindowViewModel.DockedGroups.Select(g => g.Group.Name));
    }

    /// <summary>A detached group reordered via the Dashboard (which shows every group, not just docked ones) has nothing to visually reorder in DockedGroups - Groups' own order still changes, but DockedGroups is left untouched rather than throwing.</summary>
    [Fact]
    public void ReorderGroup_SourceIsDetached_StillReordersGroups_ButLeavesDockedGroupsUnchanged()
    {
        var mainWindowViewModel = CreateViewModel();
        mainWindowViewModel.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Bravo", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        var alpha = mainWindowViewModel.Groups[0];
        var bravo = mainWindowViewModel.Groups[1];
        mainWindowViewModel.DetachGroup(alpha);

        mainWindowViewModel.ReorderGroup(alpha, bravo, insertAfter: true);

        Assert.Equal(new[] { "Bravo", "Alpha" }, mainWindowViewModel.Groups.Select(g => g.Group.Name));
        Assert.Equal(new[] { "Bravo" }, mainWindowViewModel.DockedGroups.Select(g => g.Group.Name));
    }
}
