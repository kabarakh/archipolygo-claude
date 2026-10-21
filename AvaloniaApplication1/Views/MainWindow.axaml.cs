using System;
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

public partial class MainWindow : Window, IGroupHostWindow
{
    /// <summary>On screen only while its tab is selected and the Dashboard isn't covering the tab area.</summary>
    public bool IsShowingGroup(Guid groupId) =>
        DataContext is MainWindowViewModel { IsDashboardVisible: false, SelectedGroup: { } selected } && selected.Group.Id == groupId;

    /// <summary>Set by <see cref="App"/>; null only in tests that never detach/re-dock.</summary>
    public IGroupWindowLocator? GroupWindowLocator { get; set; }

    /// <summary>Set by <see cref="App"/>; null in tests/TestHarness so headless runs never touch the real Dock/taskbar.</summary>
    public IUnreadBadgeService? UnreadBadgeService { get; set; }

    private MainWindowViewModel? _badgeSource;

    private readonly Dictionary<GroupViewModel, DetachedGroupWindow> _detachedWindows = new();

    public MainWindow()
    {
        InitializeComponent();

        // Re-apply the overlay once the taskbar button exists (Opened) and after every DPI change:
        // Avalonia's Win32 backend clears the overlay itself right before raising ScalingChanged.
        DataContextChanged += (_, _) => HookBadgeSource();
        Opened += (_, _) => ApplyBadges();
        ScalingChanged += (_, _) => ApplyWindowBadge();

        // DashboardView can't open dialogs itself (no Window, no MainWindowViewModel), so it raises events.
        DashboardViewControl.AddSlotRequested += (_, group) => _ = AddSlotToGroupAsync(group);
        DashboardViewControl.EditServerRequested += (_, group) => _ = EditServerAsync(group);
        DashboardViewControl.RemoveServerRequested += (_, group) => _ = ViewModel.RemoveGroupAsync(group);

        // Safe if already detached (DetachGroup guards it).
        DashboardViewControl.OpenInNewWindowRequested += (_, group) => ViewModel.DetachGroup(group);
        DashboardViewControl.ToggleMuteRequested += (_, group) => ViewModel.ToggleNotificationsMuted(group);

        // Detached windows aren't persisted; close them explicitly rather than relying on app shutdown.
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
    /// The window's Closed re-docks as a fallback for a plain user close; safe unconditionally because
    /// RedockGroup and the dictionary removal are both idempotent.
    /// </summary>
    public void OpenDetachedGroupWindow(GroupViewModel group)
    {
        if (GroupWindowLocator is null)
        {
            return;
        }

        var window = DetachedGroupWindow.Create(group, GroupWindowLocator, ViewModel.RedockGroup, UnreadBadgeService);
        _detachedWindows[group] = window;

        window.Closed += (_, _) =>
        {
            _detachedWindows.Remove(group);
            ViewModel.RedockGroup(group);
        };

        window.Show();
    }

    // No-op if untracked: never opened, or already removed by the window's own Closed handler.
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
            ViewModel.AddNewGroup(result.Name, result.Host, result.Port, result.Password, result.SlotName, result.AutoConnect, result.Color, result.TrackerReferenceInput, result.TrackerId, result.IsAdmin);
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

    private async Task AddSlotToGroupAsync(GroupViewModel group)
    {
        // May briefly connect/disconnect if this server has no live session right now.
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
            await ViewModel.UpdateGroup(group, result.Name, result.Host, result.Port, result.Password, result.AutoConnect, result.PreferredLeaderSlotId, result.Color, result.SlotsToRemove, result.TrackerReferenceInput, result.TrackerId, result.NotificationsMuted, result.IsAdmin);
        }
    }

    private void OnUpdateBadgeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            FlyoutBase.ShowAttachedFlyout(control);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var settingsViewModel = SettingsViewModel.FromSettings(ViewModel.LoadSettings(), ViewModel.CheckForUpdatesAsync, ViewModel.ShowUnmanagedInstallHint, ViewModel.ReadDiagnosticLog,
            dataManagement: ViewModel.CreateDataManagementContext());
        var settings = await SettingsWindow.ShowDialogAsync(this, settingsViewModel);
        if (settings is not null)
        {
            ViewModel.SaveSettings(settings);
        }
    }

    // Tab reordering: nothing is marked Handled on left press, so TabControl's click-to-select keeps working.

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

        // Right-click: context menu only - don't arm a drag, and don't let TabControl select the tab.
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

        // One DoDragDropAsync per press; later PointerMoved calls are no-ops.
        _groupTabDragStarted = true;
        await GroupReorderDragDrop.StartDragAsync(pressedArgs, group);
        group.DropIndicator = DropIndicatorPosition.None;
    }

    // Must disarm on release: PointerMoved also fires on plain hover, so a leftover candidate would start a phantom drag.
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
        // Left half = insert before, right half = after.
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

    // A menu item rather than drag-out-of-strip: dragging a tab into a new native window proved unreliable.
    private void OnDetachTabMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: GroupViewModel group })
        {
            ViewModel.DetachGroup(group);
        }
    }

    private void HookBadgeSource()
    {
        if (_badgeSource is not null)
        {
            _badgeSource.PropertyChanged -= OnBadgeSourcePropertyChanged;
        }

        _badgeSource = DataContext as MainWindowViewModel;
        if (_badgeSource is not null)
        {
            _badgeSource.PropertyChanged += OnBadgeSourcePropertyChanged;
        }
    }

    private void OnBadgeSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.MainWindowBadgeCount))
        {
            ApplyWindowBadge();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.AppBadgeCount))
        {
            UnreadBadgeService?.SetAppBadge(_badgeSource?.AppBadgeCount ?? 0);
        }
    }

    private void ApplyWindowBadge() => UnreadBadgeService?.SetWindowBadge(this, _badgeSource?.MainWindowBadgeCount ?? 0);

    private void ApplyBadges()
    {
        ApplyWindowBadge();
        UnreadBadgeService?.SetAppBadge(_badgeSource?.AppBadgeCount ?? 0);
    }

    private void OnToggleMuteMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: GroupViewModel group })
        {
            ViewModel.ToggleNotificationsMuted(group);
        }
    }
}
