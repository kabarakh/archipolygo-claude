using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// One tab = one Archipelago server. Several configured slots share one live
/// connection (the leader, <see cref="LeaderSlotId"/>); events, hints and items
/// of every slot are merged here, each tagged with its slot for filtering.
/// Connection handling is delegated to <see cref="IConnectionManager"/>.
/// </summary>
public partial class GroupViewModel : ViewModelBase, IEventItemClassFilter
{
    private readonly IConnectionManager _connectionManager;

    /// <summary>Optional; null makes <see cref="RefreshMultiworldProgressAsync"/> a no-op.</summary>
    private readonly IMultiworldTrackerService? _multiworldTrackerService;

    [ObservableProperty]
    private ServerConnectionGroup _group;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether this group lives in its own window. Runtime-only, never persisted.</summary>
    [ObservableProperty]
    private bool _isDetached;

    /// <summary>Insertion-line hint during a tab-reorder drag; UI-only, set by the DragOver/DragLeave/Drop handlers.</summary>
    [ObservableProperty]
    private DropIndicatorPosition _dropIndicator = DropIndicatorPosition.None;

    public bool IsLeaderConnected => LeaderSlotId is not null && ConnectionState == ConnectionState.Connected;

    public bool CanDisconnect => LeaderSlotId is not null || ConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting;

    /// <summary>Connect button shown instead of Disconnect while offline; needs a slot to connect as.</summary>
    public bool CanConnect => !CanDisconnect && Slots.Count > 0;

    public string HeaderText => Group.Name;

    /// <summary>
    /// The group color resolved for the current theme; falls back to the first palette
    /// color if unparseable. Re-raised on a color change and on a theme switch.
    /// </summary>
    public IBrush ColorBrush
    {
        get
        {
            var isLightTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Light;
            var displayColor = ServerColorPalette.ResolveDisplayColor(Group.Color, isLightTheme);

            // Avalonia 12.0.4's Brush has only a throwing Parse(string), no TryParse.
            try
            {
                return Brush.Parse(displayColor);
            }
            catch (Exception)
            {
                return Brush.Parse(ServerColorPalette.Colors[0]);
            }
        }
    }

    /// <summary>Backs the "Hint..." flyout; one per group so its state persists across opens.</summary>
    public HintPickerViewModel HintPicker { get; }

    /// <summary>Which filter bars are expanded; shared across all tabs and the Dashboard.</summary>
    public FilterBarLayoutState FilterLayout { get; }

    /// <summary>Admin view state; exists for every group, only reachable while <see cref="ShowAdminButton"/> is true.</summary>
    public AdminPanelViewModel Admin { get; }

    public GroupViewModel(ServerConnectionGroup group, IConnectionManager connectionManager, IMultiworldTrackerService? multiworldTrackerService = null,
        FilterBarLayoutState? filterLayout = null)
    {
        _group = group;
        FilterLayout = filterLayout ?? new FilterBarLayoutState();
        _connectionManager = connectionManager;
        _multiworldTrackerService = multiworldTrackerService;
        HintPicker = new HintPickerViewModel(this, connectionManager);
        Admin = new AdminPanelViewModel(this, connectionManager, multiworldTrackerService);

        Events.CollectionChanged += OnEventsCollectionChanged;
        Hints.CollectionChanged += OnHintsCollectionChanged;
        ReceivedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(VisibleReceivedItems));
        Group.PropertyChanged += OnGroupPropertyChanged;

        // Never unsubscribed: a GroupViewModel lives as long as its server entry.
        if (Application.Current is not null)
        {
            Application.Current.ActualThemeVariantChanged += (_, _) => OnPropertyChanged(nameof(ColorBrush));
        }

        RefreshSlotOrder();

        foreach (var slot in Group.Slots)
        {
            SubscribeSlotProgress(slot);
        }

        Group.Slots.CollectionChanged += OnSlotsCollectionChanged;
    }

    partial void OnGroupChanged(ServerConnectionGroup? oldValue, ServerConnectionGroup newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnGroupPropertyChanged;
        }

        newValue.PropertyChanged += OnGroupPropertyChanged;
        RaiseHeaderChanged();
        RaiseMuteStateChanged();
        OnPropertyChanged(nameof(Slots));
    }

    partial void OnConnectionStateChanged(ConnectionState value) => RaiseConnectionAvailabilityChanged();

    /// <summary>Re-evaluates everything that depends on "is there a live leader right now".</summary>
    private void RaiseConnectionAvailabilityChanged()
    {
        OnPropertyChanged(nameof(IsLeaderConnected));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanConnect));
        SendMessageCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
    }

    private void RaiseHeaderChanged()
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(DetachedWindowTitle));
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ServerConnectionGroup.Name):
                RaiseHeaderChanged();
                break;
            case nameof(ServerConnectionGroup.NotificationsMuted):
                RaiseMuteStateChanged();
                break;
            case nameof(ServerConnectionGroup.Color):
                OnPropertyChanged(nameof(ColorBrush));
                break;
            case nameof(ServerConnectionGroup.IsAdmin):
                OnIsAdminChanged();
                break;
            case nameof(ServerConnectionGroup.TrackerId):
                OnTrackerIdChanged();
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() =>
        // DisconnectGroupAsync resets SelectedChatSlot itself, without re-entering OnSelectedChatSlotChanged.
        _connectionManager.DisconnectGroupAsync(this);

    /// <summary>Connects as the same slot startup would pick (<see cref="ServerConnectionGroup.ResolveStartupLeader"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() =>
        Group.ResolveStartupLeader() is { } targetSlot
            ? _connectionManager.SwitchLeaderAsync(this, targetSlot)
            : Task.CompletedTask;
}
