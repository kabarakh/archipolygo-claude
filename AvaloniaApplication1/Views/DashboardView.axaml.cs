using System;
using Archipolygo.Models;
using Archipolygo.ViewModels;
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

    /// <summary>
    /// Overview row clicked - selects that server's tab and leaves the
    /// dashboard (see <see cref="DashboardViewModel.SelectGroupAndLeaveDashboard"/>).
    /// Clears the selection right back out afterward so the same row can be
    /// clicked again next time the dashboard is shown - otherwise a second
    /// click on an already-selected row wouldn't raise <see cref="ListBox.SelectionChanged"/>
    /// at all.
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

    /// <summary>Same navigation as <see cref="OnOverviewRowSelected"/>, triggered from the shared Hints list instead - each row carries its own <see cref="GroupViewModel"/> (see <see cref="DashboardHintRow"/>), so this works regardless of which server filter is currently selected.</summary>
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
