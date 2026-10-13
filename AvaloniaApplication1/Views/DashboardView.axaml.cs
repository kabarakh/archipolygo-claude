using System;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Archipolygo.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private DashboardViewModel ViewModel => (DashboardViewModel)DataContext!;

    // Row actions bubble up to MainWindow: this view has no Window to own dialogs and no MainWindowViewModel.
    public event EventHandler<GroupViewModel>? AddSlotRequested;

    public event EventHandler<GroupViewModel>? EditServerRequested;

    public event EventHandler<GroupViewModel>? RemoveServerRequested;

    public event EventHandler<GroupViewModel>? OpenInNewWindowRequested;

    public event EventHandler<GroupViewModel>? ToggleMuteRequested;

    private static GroupViewModel? GroupOf(object? sender) => (sender as Control)?.DataContext as GroupViewModel;

    private void OnAddSlotIconClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            AddSlotRequested?.Invoke(this, group);
        }
    }

    private void OnEditServerIconClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            EditServerRequested?.Invoke(this, group);
        }
    }

    private void OnRemoveServerIconClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            RemoveServerRequested?.Invoke(this, group);
        }
    }

    private void OnToggleMuteMenuClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            ToggleMuteRequested?.Invoke(this, group);
        }
    }

    // Handled here rather than bubbled up: it needs only the row's group. Anchored on this view, not the MenuItem (whose TopLevel is the popup).
    private async void OnCopyHostPortMenuClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            await ClipboardCopyHelper.CopyTextAsync(this, group.Group.HostPort);
        }
    }

    private void OnOpenInNewWindowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            OpenInNewWindowRequested?.Invoke(this, group);
        }
    }

    // Clears the selection again so clicking the same row next time still raises SelectionChanged.
    private void OnOverviewRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not GroupViewModel group)
        {
            return;
        }

        OverviewListBox.SelectedItem = null;
        ViewModel.SelectGroupAndLeaveDashboard(group);
    }

    // ListBox selects (and so navigates) on any button press; handling right-press keeps the context menu from also switching tabs.
    private void OnOverviewRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual && e.GetCurrentPoint(visual).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
        }
    }

    // Drag reordering starts only from the row's grip: a plain row press selects and navigates immediately,
    // before a drag could start. The grip is a Border, not a Button, whose press tracking would fight the drag threshold.

    private PointerPressedEventArgs? _overviewGripPressedArgs;
    private GroupViewModel? _overviewGripDragCandidate;
    private Point _overviewGripDragStartPosition;
    private ulong _overviewGripPressedTimestamp;
    private bool _overviewGripDragStarted;

    private void OnOverviewGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GroupViewModel group)
        {
            return;
        }

        // Keeps the press from reaching the ListBoxItem's selection/navigation.
        e.Handled = true;
        _overviewGripPressedArgs = e;
        _overviewGripDragCandidate = group;
        _overviewGripDragStartPosition = e.GetPosition(control);
        _overviewGripPressedTimestamp = e.Timestamp;
        _overviewGripDragStarted = false;
    }

    private async void OnOverviewGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_overviewGripDragCandidate is not { } group || _overviewGripDragStarted ||
            _overviewGripPressedArgs is not { } pressedArgs || sender is not Control control)
        {
            return;
        }

        if (!GroupReorderDragDrop.ShouldStartDrag(_overviewGripDragStartPosition, _overviewGripPressedTimestamp, e.GetPosition(control), e.Timestamp))
        {
            return;
        }

        // One DoDragDropAsync per press; later PointerMoved calls are no-ops.
        _overviewGripDragStarted = true;
        await GroupReorderDragDrop.StartDragAsync(pressedArgs, group);
        group.DropIndicator = DropIndicatorPosition.None;
    }

    // Without disarming here, a later plain hover (PointerMoved fires without a button held) would start a phantom drag.
    private void OnOverviewGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _overviewGripPressedArgs = null;
        _overviewGripDragCandidate = null;
        _overviewGripDragStarted = false;
    }

    private void OnOverviewRowDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GroupViewModel target } control)
        {
            return;
        }

        var isValidTarget = GroupReorderDragDrop.TryGetSource(e.DataTransfer) is { } source && !ReferenceEquals(source, target);
        e.DragEffects = isValidTarget ? DragDropEffects.Move : DragDropEffects.None;
        // Top half = insert before, bottom half = after.
        target.DropIndicator = !isValidTarget
            ? DropIndicatorPosition.None
            : e.GetPosition(control).Y < control.Bounds.Height / 2
                ? DropIndicatorPosition.Before
                : DropIndicatorPosition.After;
    }

    private void OnOverviewRowDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: GroupViewModel target })
        {
            target.DropIndicator = DropIndicatorPosition.None;
        }
    }

    private void OnOverviewRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GroupViewModel target })
        {
            return;
        }

        var insertAfter = target.DropIndicator == DropIndicatorPosition.After;
        target.DropIndicator = DropIndicatorPosition.None;
        if (GroupReorderDragDrop.TryGetSource(e.DataTransfer) is { } source)
        {
            ViewModel.ReorderGroup(source, target, insertAfter);
        }
    }

    private void OnHintRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DashboardHintRow row)
        {
            return;
        }

        DashboardHintsListBox.SelectedItem = null;
        ViewModel.SelectGroupAndLeaveDashboard(row.Group);
    }

    private async void OnDashboardEventsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ClipboardCopyHelper.IsCopyShortcut(e) || sender is not ListBox listBox)
        {
            return;
        }

        var copied = await ClipboardCopyHelper.CopySelectedLinesAsync<DashboardEventRow>(this, listBox,
            row => $"{row.Group.HeaderText}: {row.Event.Text}");
        if (copied)
        {
            e.Handled = true;
        }
    }
}
