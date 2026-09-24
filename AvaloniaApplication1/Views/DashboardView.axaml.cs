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

    /// <summary>
    /// Per-row "Add slot..." icon clicked (see Feature-Plaene/Archiv/Dashboard-Tab.md's
    /// Status note). This view has neither a Window to anchor the
    /// <c>ConnectionEditorWindow</c> dialog on nor a reference to
    /// <see cref="MainWindowViewModel"/> (its own <see cref="DataContext"/>
    /// is the narrower <see cref="DashboardViewModel"/>) - it only reports
    /// which row's group was clicked; <see cref="MainWindow"/> subscribes to
    /// this and runs the exact same dialog flow the toolbar's own "Add
    /// slot..." button uses.
    /// </summary>
    public event EventHandler<GroupViewModel>? AddSlotRequested;

    /// <summary>Per-row "Edit server..." icon clicked - see <see cref="AddSlotRequested"/>'s doc comment.</summary>
    public event EventHandler<GroupViewModel>? EditServerRequested;

    /// <summary>Per-row "Remove server" icon clicked - see <see cref="AddSlotRequested"/>'s doc comment.</summary>
    public event EventHandler<GroupViewModel>? RemoveServerRequested;

    /// <summary>
    /// Per-row "Open in new window" context-menu item clicked (Feature-Plaene/
    /// Tab-Eigenes-Fenster.md) - see <see cref="AddSlotRequested"/>'s doc
    /// comment for why this bubbles up rather than acting here directly
    /// (detaching needs <see cref="MainWindowViewModel.DetachGroup"/>, which
    /// this view has no reference to).
    /// </summary>
    public event EventHandler<GroupViewModel>? OpenInNewWindowRequested;

    /// <summary>
    /// Every per-row icon button's own <c>DataContext</c> is already the
    /// row's <see cref="GroupViewModel"/> (inherited from the enclosing
    /// <c>DataTemplate</c>), so no <c>CommandParameter</c>/<c>RelativeSource</c>
    /// indirection is needed to recover it here.
    /// </summary>
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

    private void OnOpenInNewWindowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (GroupOf(sender) is { } group)
        {
            OpenInNewWindowRequested?.Invoke(this, group);
        }
    }

    /// <summary>
    /// Overview row clicked - selects that server's tab and leaves the
    /// dashboard (see <see cref="DashboardViewModel.SelectGroupAndLeaveDashboard"/>).
    /// Clears the selection right back out afterward so the same row can be
    /// clicked again next time the dashboard is shown - otherwise a second
    /// click on an already-selected row wouldn't raise <see cref="ListBox.SelectionChanged"/>
    /// at all. Plain <c>SelectionChanged</c>, not a hand-rolled press/release
    /// gesture (used to be, for one version of Feature-Plaene/Tab-Reihenfolge.md's
    /// manual drag-reordering) - dragging now only ever starts from each
    /// row's own dedicated grip handle (see <see cref="OnOverviewGripPointerPressed"/>),
    /// so a plain click anywhere else on the row has no press-vs-drag
    /// ambiguity left to resolve.
    /// </summary>
    private void OnOverviewRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not GroupViewModel group)
        {
            return;
        }

        OverviewListBox.SelectedItem = null;
        ViewModel.SelectGroupAndLeaveDashboard(group);
    }

    /// <summary>
    /// A right-click should only open the row's "Open in new window"
    /// context menu (Feature-Plaene/Tab-Eigenes-Fenster.md), not also select
    /// the row - without this, Avalonia's ListBox selects a row (and so
    /// fires <see cref="OnOverviewRowSelected"/>'s navigate-away-from-
    /// Dashboard logic) on ANY pointer button press, not just the left one,
    /// so right-clicking a row both opened the menu AND switched to that
    /// server's tab (dev feedback). Marking PointerPressed itself Handled
    /// stops the ListBoxItem's own press-driven selection without affecting
    /// the context menu's opening at all - that's driven by a separate,
    /// unrelated mechanism, not gated on this event.
    /// </summary>
    private void OnOverviewRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Visual visual && e.GetCurrentPoint(visual).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
        }
    }

    // ── Overview row: manual drag-reordering (Feature-Plaene/Tab-Reihenfolge.md) ──
    //
    // Unlike MainWindow.axaml's tab headers (where the whole header is
    // draggable, since a plain click there only ever selects the tab - no
    // conflict), a plain click on an Overview row navigates away from the
    // Dashboard entirely; Avalonia selects a ListBoxItem (and so fires
    // SelectionChanged) on PointerPressed for mouse input, which would fire
    // that navigation instantly on press, before a drag gesture could ever
    // get a chance to start. So dragging is scoped to a small dedicated grip
    // handle instead (Border.drag-handle in DashboardView.axaml) - only it
    // marks PointerPressed Handled (same trick the per-row icon buttons
    // already rely on to keep their own clicks from also navigating the
    // row - their own Button press handling marks it Handled before it ever
    // reaches this row's ListBoxItem, see
    // DashboardTabTests.RowIconClick_DoesNotAlsoNavigateTheRow), which is
    // also why the grip is a plain Border and not a real Button: a Button's
    // own internal press-state tracking would fight this drag-threshold
    // detection instead of just getting out of the way.

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

        // Stops this press from reaching the row's own ListBoxItem
        // selection (and so its SelectionChanged-driven navigation) - see
        // this section's own comment above.
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

        // Consume immediately - one DoDragDropAsync per press, and this also
        // means a PointerMoved that fires again while the drag is already
        // in flight (or after it ended) is a harmless no-op above.
        _overviewGripDragStarted = true;
        await GroupReorderDragDrop.StartDragAsync(pressedArgs, group);
        group.DropIndicator = DropIndicatorPosition.None;
    }

    /// <summary>
    /// Disarms the gesture-tracking fields once the pointer comes back up -
    /// see <see cref="MainWindow.OnGroupTabPointerReleased"/>'s doc comment
    /// for why this is needed (a plain click otherwise leaves
    /// <see cref="_overviewGripDragCandidate"/> set, and a later
    /// unrelated hover move over the grip - PointerMoved fires for that
    /// too, not just movement while a button is held - would start a
    /// phantom drag).
    /// </summary>
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
        // The Overview list lays out top-to-bottom - top half of the
        // hovered row means "insert before this server", bottom half means
        // "after" (MainWindow.axaml's tab headers use the same idea, just
        // left/right since those lay out horizontally).
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

    /// <summary>Same navigation as the Overview row's own <see cref="OnOverviewRowSelected"/>, triggered from the shared Hints list instead - each row carries its own <see cref="GroupViewModel"/> (see <see cref="DashboardHintRow"/>), so this works regardless of which server filter is currently selected. Hints rows aren't drag-reorderable, so this is unaffected by any of the above.</summary>
    private void OnHintRowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DashboardHintRow row)
        {
            return;
        }

        DashboardHintsListBox.SelectedItem = null;
        ViewModel.SelectGroupAndLeaveDashboard(row.Group);
    }

    /// <summary>
    /// Ctrl/Cmd+C copies the selected rows' text - same mechanism as a
    /// server tab's own Events list (see <see cref="MainWindow.OnEventsListKeyDown"/>),
    /// sharing the actual selection-to-clipboard logic via <see cref="ClipboardCopyHelper"/>
    /// rather than each hand-rolling a copy of it. Only this list's own row
    /// type (<see cref="DashboardEventRow"/>) and line format (prefixed with
    /// the row's own server name, since this list spans several servers at
    /// once) are specific to this handler.
    /// </summary>
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
