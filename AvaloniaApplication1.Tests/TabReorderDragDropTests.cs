using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the actual DragOver/DragLeave/Drop
/// wiring in the real <c>.axaml</c> (MainWindow's tab headers, DashboardView's
/// Overview rows) - Feature-Plaene/Tab-Reihenfolge.md. Raises
/// <see cref="DragDrop"/>'s routed events directly on the target
/// <see cref="Border"/> with a hand-built <see cref="DataTransfer"/> payload
/// and an explicit position within the target (to drive the Before/After
/// insertion-line detection), rather than driving the gesture through
/// <c>PointerPressed</c>/<c>PointerMoved</c> and a real
/// <see cref="DragDrop.DoDragDropAsync"/> - see <see cref="GroupReorderTests"/>'s
/// own class doc comment for why that part is out of scope for a headless
/// run (no registered <c>IPlatformDragSource</c>). This still exercises the
/// real code-behind handlers (<see cref="MainWindow"/>'s
/// <c>OnGroupTabDragOver</c>/<c>Drop</c>, <see cref="DashboardView"/>'s
/// <c>OnOverviewRowDragOver</c>/<c>Drop</c>) against the real visual tree,
/// same spirit as <see cref="DashboardTabTests"/>.
/// </summary>
public class TabReorderDragDropTests
{
    private static MainWindow ShowWindow(MainWindowViewModel viewModel)
    {
        var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static DragEventArgs MakeDragEventArgs(RoutedEvent<DragEventArgs> routedEvent, GroupViewModel? source, Interactive target, Point targetLocation = default)
    {
        var dataTransfer = new DataTransfer();
        if (source is not null)
        {
            dataTransfer.Add(DataTransferItem.Create(GroupReorderDragDrop.Format, source));
        }

        return new DragEventArgs(routedEvent, dataTransfer, target, targetLocation, KeyModifiers.None);
    }

    private static Border FindTabHeader(MainWindow window, GroupViewModel group) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => (string?)b.Tag == "GroupTabHeader" && b.DataContext == group);

    /// <summary>A point in the left half of <paramref name="control"/>'s own bounds - "insert before" for a horizontally-laid-out tab header.</summary>
    private static Point LeftHalf(Control control) => new(2, control.Bounds.Height / 2);

    /// <summary>A point in the right half of <paramref name="control"/>'s own bounds - "insert after" for a horizontally-laid-out tab header.</summary>
    private static Point RightHalf(Control control) => new(control.Bounds.Width - 2, control.Bounds.Height / 2);

    /// <summary>A point in the top half of <paramref name="control"/>'s own bounds - "insert before" for a vertically-laid-out Overview row.</summary>
    private static Point TopHalf(Control control) => new(control.Bounds.Width / 2, 2);

    /// <summary>A point in the bottom half of <paramref name="control"/>'s own bounds - "insert after" for a vertically-laid-out Overview row.</summary>
    private static Point BottomHalf(Control control) => new(control.Bounds.Width / 2, control.Bounds.Height - 2);

    private static (MainWindowViewModel viewModel, MainWindow window, GroupViewModel group1, GroupViewModel group2) SetUpTwoGroups()
    {
        var viewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        viewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = viewModel.Groups[0];
        var group2 = viewModel.Groups[1];

        var window = ShowWindow(viewModel);
        viewModel.IsDashboardVisible = false; // tab strip only materializes once it's the visible side (CLAUDE.md's lazy-ContentTemplate gotcha).
        Dispatcher.UIThread.RunJobs();

        return (viewModel, window, group1, group2);
    }

    [AvaloniaFact]
    public void TabHeader_DragOverLeftHalfOfAnotherTab_ShowsBeforeIndicator_AndAcceptsMove()
    {
        var (_, window, group1, group2) = SetUpTwoGroups();
        var targetHeader = FindTabHeader(window, group2);

        var dragOver = MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetHeader, LeftHalf(targetHeader));
        targetHeader.RaiseEvent(dragOver);

        Assert.Equal(DragDropEffects.Move, dragOver.DragEffects);
        Assert.Equal(DropIndicatorPosition.Before, group2.DropIndicator);
        Assert.Equal(DropIndicatorPosition.None, group1.DropIndicator);
    }

    [AvaloniaFact]
    public void TabHeader_DragOverRightHalfOfAnotherTab_ShowsAfterIndicator()
    {
        var (_, window, group1, group2) = SetUpTwoGroups();
        var targetHeader = FindTabHeader(window, group2);

        var dragOver = MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetHeader, RightHalf(targetHeader));
        targetHeader.RaiseEvent(dragOver);

        Assert.Equal(DragDropEffects.Move, dragOver.DragEffects);
        Assert.Equal(DropIndicatorPosition.After, group2.DropIndicator);
    }

    [AvaloniaFact]
    public void TabHeader_DragOverItself_RejectsTheDrop_AndDoesNotShowAnIndicator()
    {
        var (_, window, group1, _) = SetUpTwoGroups();
        var ownHeader = FindTabHeader(window, group1);

        var dragOver = MakeDragEventArgs(DragDrop.DragOverEvent, group1, ownHeader, LeftHalf(ownHeader));
        ownHeader.RaiseEvent(dragOver);

        Assert.Equal(DragDropEffects.None, dragOver.DragEffects);
        Assert.Equal(DropIndicatorPosition.None, group1.DropIndicator);
    }

    [AvaloniaFact]
    public void TabHeader_DragLeave_ClearsTheIndicator()
    {
        var (_, window, group1, group2) = SetUpTwoGroups();
        var targetHeader = FindTabHeader(window, group2);

        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetHeader, LeftHalf(targetHeader)));
        Assert.Equal(DropIndicatorPosition.Before, group2.DropIndicator);

        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DragLeaveEvent, group1, targetHeader));
        Assert.Equal(DropIndicatorPosition.None, group2.DropIndicator);
    }

    [AvaloniaFact]
    public void TabHeader_DropOnLeftHalf_InsertsSourceBeforeTarget_AndClearsTheIndicator()
    {
        var (viewModel, window, group1, group2) = SetUpTwoGroups();
        var targetHeader = FindTabHeader(window, group2);

        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetHeader, LeftHalf(targetHeader)));
        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DropEvent, group1, targetHeader, LeftHalf(targetHeader)));

        // group1 was already right before group2 - "insert before" is a no-op move here.
        Assert.Equal(new[] { "Server1", "Server2" }, viewModel.Groups.Select(g => g.Group.Name));
        Assert.Equal(DropIndicatorPosition.None, group2.DropIndicator);
    }

    [AvaloniaFact]
    public void TabHeader_DropOnRightHalf_InsertsSourceAfterTarget()
    {
        var (viewModel, window, group1, group2) = SetUpTwoGroups();
        var targetHeader = FindTabHeader(window, group2);

        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetHeader, RightHalf(targetHeader)));
        targetHeader.RaiseEvent(MakeDragEventArgs(DragDrop.DropEvent, group1, targetHeader, RightHalf(targetHeader)));

        Assert.Equal(new[] { "Server2", "Server1" }, viewModel.Groups.Select(g => g.Group.Name));
        Assert.Equal(DropIndicatorPosition.None, group2.DropIndicator);
    }

    [AvaloniaFact]
    public void OverviewRow_DropOnBottomHalf_InsertsSourceAfterTarget_ThroughTheDashboardViewModelCallback()
    {
        var viewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        viewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = viewModel.Groups[0];
        var group2 = viewModel.Groups[1];

        var window = ShowWindow(viewModel);
        viewModel.IsDashboardVisible = true; // Overview is the default view already, but explicit for clarity.
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var targetRow = dashboardView.GetVisualDescendants().OfType<Border>().Single(b => (string?)b.Tag == "OverviewRow" && b.DataContext == group2);

        targetRow.RaiseEvent(MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetRow, BottomHalf(targetRow)));
        Assert.Equal(DropIndicatorPosition.After, group2.DropIndicator);

        targetRow.RaiseEvent(MakeDragEventArgs(DragDrop.DropEvent, group1, targetRow, BottomHalf(targetRow)));

        Assert.Equal(new[] { "Server2", "Server1" }, viewModel.Groups.Select(g => g.Group.Name));
        Assert.Equal(DropIndicatorPosition.None, group2.DropIndicator);
        // Dropping must never also navigate away from the Dashboard - that's
        // OnOverviewRowSelected's job (a plain row click, no drag involved),
        // untouched by this test.
        Assert.True(viewModel.IsDashboardVisible);
    }

    [AvaloniaFact]
    public void OverviewRow_DropOnTopHalf_InsertsSourceBeforeTarget()
    {
        var viewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        viewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        viewModel.AddNewGroup("Server3", "host3", 3, string.Empty, "Carol", autoConnect: false);
        var group1 = viewModel.Groups[0];
        var group3 = viewModel.Groups[2];

        var window = ShowWindow(viewModel);
        viewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var targetRow = dashboardView.GetVisualDescendants().OfType<Border>().Single(b => (string?)b.Tag == "OverviewRow" && b.DataContext == group3);

        targetRow.RaiseEvent(MakeDragEventArgs(DragDrop.DragOverEvent, group1, targetRow, TopHalf(targetRow)));
        Assert.Equal(DropIndicatorPosition.Before, group3.DropIndicator);

        targetRow.RaiseEvent(MakeDragEventArgs(DragDrop.DropEvent, group1, targetRow, TopHalf(targetRow)));

        Assert.Equal(new[] { "Server2", "Server1", "Server3" }, viewModel.Groups.Select(g => g.Group.Name));
    }
}
