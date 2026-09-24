using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Set by <see cref="App"/> right after construction, same wiring style
    /// as <see cref="MainWindowViewModel.ShowPasswordPromptDialogAsync"/> -
    /// needed by <see cref="OpenDetachedGroupWindow"/>/
    /// <see cref="CloseDetachedGroupWindowIfOpen"/> below (Feature-Plaene/
    /// Tab-Eigenes-Fenster.md). Null only in a test construction site that
    /// never exercises detach/re-dock.
    /// </summary>
    public IGroupWindowLocator? GroupWindowLocator { get; set; }

    /// <summary>Every currently-open detached window, keyed by the group it shows - see <see cref="OpenDetachedGroupWindow"/>.</summary>
    private readonly Dictionary<GroupViewModel, DetachedGroupWindow> _detachedWindows = new();

    public MainWindow()
    {
        InitializeComponent();

        // Feature-Plaene/Archiv/Dashboard-Tab.md's per-row icons: DashboardView
        // itself has no Window to anchor a dialog on and no reference to
        // MainWindowViewModel (its own DataContext is the narrower
        // DashboardViewModel) - it just raises which group was clicked, and
        // this class runs the exact same dialog flow as the toolbar buttons
        // below, just parameterized by that group instead of always
        // ViewModel.SelectedGroup.
        DashboardViewControl.AddSlotRequested += (_, group) => _ = AddSlotToGroupAsync(group);
        DashboardViewControl.EditServerRequested += (_, group) => _ = EditServerAsync(group);
        DashboardViewControl.RemoveServerRequested += (_, group) => _ = ViewModel.RemoveGroupAsync(group);

        // Feature-Plaene/Tab-Eigenes-Fenster.md: the Dashboard's own
        // "Open in new window" row context-menu item - same detach, just
        // triggered from the Overview instead of a tab header (see
        // OnDetachTabMenuClick). A no-op if the group is already detached
        // (DetachGroup's own guard) - the Dashboard row's menu item is
        // greyed out for that case, but this stays safe regardless.
        DashboardViewControl.OpenInNewWindowRequested += (_, group) => ViewModel.DetachGroup(group);

        // Feature-Plaene/Tab-Eigenes-Fenster.md: no detached window's own
        // state is persisted, so there's nothing to save here - closing the
        // main window ends the whole app (the Avalonia default), and every
        // still-open detached window just needs to be closed explicitly
        // first rather than relying on that shutdown to do it silently.
        Closing += (_, _) =>
        {
            foreach (var window in _detachedWindows.Values.ToList())
            {
                window.Close();
            }
        };
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    /// <summary>
    /// <see cref="MainWindowViewModel.OpenDetachedWindow"/>'s real
    /// implementation - creates and shows a <see cref="DetachedGroupWindow"/>
    /// for <paramref name="group"/>, tracked so <see cref="CloseDetachedGroupWindowIfOpen"/>
    /// can find it again (from its own "Dock to main window" button) and so
    /// <see cref="Closing"/> above can sweep it up on app exit. Its own
    /// <see cref="Window.Closed"/> re-docks the group as a fallback for a
    /// plain user close (the "X" button/Alt+F4, as opposed to the "Dock to
    /// main window" button, which already called
    /// <see cref="MainWindowViewModel.RedockGroup"/> itself before this ever
    /// runs) - safe to call unconditionally either way, since both
    /// <see cref="MainWindowViewModel.RedockGroup"/> and the dictionary
    /// removal below are no-ops once already applied.
    /// </summary>
    public void OpenDetachedGroupWindow(GroupViewModel group)
    {
        if (GroupWindowLocator is null)
        {
            return;
        }

        var window = DetachedGroupWindow.Create(group, GroupWindowLocator, ViewModel.RedockGroup);
        _detachedWindows[group] = window;

        window.Closed += (_, _) =>
        {
            _detachedWindows.Remove(group);
            ViewModel.RedockGroup(group);
        };

        window.Show();
    }

    /// <summary>
    /// <see cref="MainWindowViewModel.CloseDetachedWindow"/>'s real
    /// implementation - closes a group's already-open detached window (its
    /// own "Dock to main window" button, or the server being removed
    /// entirely while detached). No-op if none is tracked - either it was
    /// never open, or its own <see cref="Window.Closed"/> handler (see
    /// <see cref="OpenDetachedGroupWindow"/>) already removed it from
    /// <see cref="_detachedWindows"/> for a plain user close that's what
    /// triggered this call in the first place.
    /// </summary>
    public void CloseDetachedGroupWindowIfOpen(GroupViewModel group)
    {
        if (_detachedWindows.Remove(group, out var window))
        {
            window.Close();
        }
    }

    private async void OnAddServerClick(object? sender, RoutedEventArgs e)
    {
        var defaultAutoConnect = ViewModel.LoadSettings().DefaultAutoConnect;
        var editorViewModel = ConnectionEditorViewModel.ForNewGroup(defaultAutoConnect, ViewModel.GetAllGroups(), ViewModel.ResolveTrackerIdAsync, ViewModel.ResolveRoomConnectionInfoAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddNewGroup(result.Name, result.Host, result.Port, result.Password, result.SlotName, result.AutoConnect, result.Color, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    private async void OnAddSlotClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is not null)
        {
            await AddSlotToGroupAsync(selectedGroup);
        }
    }

    /// <summary>Shared by the toolbar's "Add slot..." (always <see cref="MainWindowViewModel.SelectedGroup"/>) and the Dashboard's per-row icon (whichever group's row was clicked).</summary>
    private async Task AddSlotToGroupAsync(GroupViewModel group)
    {
        // May briefly connect/disconnect under the hood if this server has
        // no live session right now - see MainWindowViewModel.GetAvailableSlotsToAddAsync.
        var availablePlayers = await ViewModel.GetAvailableSlotsToAddAsync(group);

        var editorViewModel = ConnectionEditorViewModel.ForAddSlot(group.Group, availablePlayers);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            ViewModel.AddSlotsToGroup(group, result.SlotsToAdd);
        }
    }

    private async void OnEditServerClick(object? sender, RoutedEventArgs e)
    {
        var selectedGroup = ViewModel.SelectedGroup;
        if (selectedGroup is not null)
        {
            await EditServerAsync(selectedGroup);
        }
    }

    /// <summary>Shared by the toolbar's "Edit server..." and the Dashboard's per-row icon - see <see cref="AddSlotToGroupAsync"/>'s doc comment.</summary>
    private async Task EditServerAsync(GroupViewModel group)
    {
        var editorViewModel = ConnectionEditorViewModel.ForEditGroup(
            group.Group,
            ViewModel.GetAllGroups(),
            ViewModel.ResolveTrackerIdAsync,
            ViewModel.ResolveRoomConnectionInfoAsync);
        var result = await ConnectionEditorWindow.ShowDialogAsync(this, editorViewModel);
        if (result is not null)
        {
            await ViewModel.UpdateGroup(group, result.Name, result.Host, result.Port, result.Password, result.AutoConnect, result.PreferredLeaderSlotId, result.Color, result.SlotsToRemove, result.TrackerReferenceInput, result.TrackerId);
        }
    }

    /// <summary>
    /// Feature-Plaene/Archiv/Auto-Update.md's update-available button (a
    /// small barely-visible dot originally - now a real labeled Button, see
    /// MainWindow.axaml) - opens its attached Flyout with the version and
    /// the actual "Update now" button.
    /// </summary>
    private void OnUpdateBadgeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsViewModel = SettingsViewModel.FromSettings(ViewModel.LoadSettings(), ViewModel.CheckForUpdatesAsync, ViewModel.ShowUnmanagedInstallHint, ViewModel.ReadDiagnosticLog);
        var settings = await SettingsWindow.ShowDialogAsync(this, settingsViewModel);
        if (settings is not null)
        {
            ViewModel.SaveSettings(settings);
        }
    }

    // ── Manual tab-reordering (Feature-Plaene/Tab-Reihenfolge.md) ──
    //
    // A plain tab click must keep working exactly as before (TabControl
    // already selects a tab on PointerPressed) - unlike DashboardView's
    // Overview rows (see DashboardView.axaml.cs), nothing here is ever
    // marked Handled on press, so this only ever adds drag detection on
    // top of the existing click behavior, never replaces it.

    private PointerPressedEventArgs? _groupTabPressedArgs;
    private GroupViewModel? _groupTabDragCandidate;
    private Point _groupTabDragStartPosition;
    private ulong _groupTabPressedTimestamp;
    private bool _groupTabDragStarted;

    private void OnGroupTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not GroupViewModel group)
        {
            return;
        }

        // A right-click should only open the "Open in new window" context
        // menu (Feature-Plaene/Tab-Eigenes-Fenster.md), never arm the
        // reorder-drag gesture below - without this, right-clicking a tab
        // both opened the menu AND started tracking a drag (dev feedback:
        // the drag-drop cursor showed up), since this handler used to arm
        // unconditionally regardless of which button was pressed. Marking
        // it Handled here also stops it from reaching TabControl's own
        // press-selects-tab behavior, so a right-click never switches the
        // active tab either - see DashboardView.OnOverviewRowPointerPressed
        // for the same fix applied to the Dashboard's row context menu.
        if (e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            return;
        }

        _groupTabPressedArgs = e;
        _groupTabDragCandidate = group;
        _groupTabDragStartPosition = e.GetPosition(control);
        _groupTabPressedTimestamp = e.Timestamp;
        _groupTabDragStarted = false;
    }

    private async void OnGroupTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_groupTabDragCandidate is not { } group || _groupTabDragStarted ||
            _groupTabPressedArgs is not { } pressedArgs || sender is not Control control)
        {
            return;
        }

        if (!GroupReorderDragDrop.ShouldStartDrag(_groupTabDragStartPosition, _groupTabPressedTimestamp, e.GetPosition(control), e.Timestamp))
        {
            return;
        }

        // Consume immediately - one DoDragDropAsync per press, and this also
        // means a PointerMoved that fires again while the drag is already
        // in flight (or after it ended) is a harmless no-op above.
        _groupTabDragStarted = true;
        await GroupReorderDragDrop.StartDragAsync(pressedArgs, group);
        group.DropIndicator = DropIndicatorPosition.None;
    }

    /// <summary>
    /// Disarms the gesture-tracking fields once the pointer comes back up -
    /// without this, a plain click (press then release, no real drag)
    /// left <see cref="_groupTabDragCandidate"/> set, and
    /// <see cref="OnGroupTabPointerMoved"/> fires for plain hover movement
    /// too, not just movement while a button is held. Once enough real time
    /// had simply passed and the cursor had moved anywhere at all - true
    /// for basically any later hover, unrelated to the original click - a
    /// phantom drag started despite no button being pressed anymore (dev
    /// feedback: clicking a tab then just moving the mouse afterward showed
    /// a drag cursor). <see cref="OnGroupTabPointerPressed"/> re-arms it
    /// fresh on the next real press.
    /// </summary>
    private void OnGroupTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _groupTabPressedArgs = null;
        _groupTabDragCandidate = null;
        _groupTabDragStarted = false;
    }

    private void OnGroupTabDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GroupViewModel target } control)
        {
            return;
        }

        var isValidTarget = GroupReorderDragDrop.TryGetSource(e.DataTransfer) is { } source && !ReferenceEquals(source, target);
        e.DragEffects = isValidTarget ? DragDropEffects.Move : DragDropEffects.None;
        // Tab headers lay out left-to-right - left half of the hovered
        // header means "insert before this tab", right half means "after".
        target.DropIndicator = !isValidTarget
            ? DropIndicatorPosition.None
            : e.GetPosition(control).X < control.Bounds.Width / 2
                ? DropIndicatorPosition.Before
                : DropIndicatorPosition.After;
    }

    private void OnGroupTabDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: GroupViewModel target })
        {
            target.DropIndicator = DropIndicatorPosition.None;
        }
    }

    private void OnGroupTabDrop(object? sender, DragEventArgs e)
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

    /// <summary>
    /// Feature-Plaene/Tab-Eigenes-Fenster.md's "Open in new window" tab
    /// context-menu item - detaching used to be a drag gesture (drop outside
    /// the tab strip), replaced by this menu item because dragging a tab
    /// clean out of the TabControl into a brand-new native window turned out
    /// unreliable in practice (dev feedback: couldn't pull a tab out at
    /// all). <see cref="MainWindowViewModel.RedockGroup"/>'s own trigger
    /// (dragging a <see cref="DetachedGroupWindow"/> back) was replaced the
    /// same way, by that window's own "Dock to main window" button.
    /// </summary>
    private void OnDetachTabMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: GroupViewModel group })
        {
            ViewModel.DetachGroup(group);
        }
    }
}
