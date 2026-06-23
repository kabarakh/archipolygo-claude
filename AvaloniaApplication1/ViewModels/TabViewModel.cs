using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// Represents a tab in the MainWindow, i.e. a connection to one slot.
/// Holds the profile, connection state, event log and hint list; actual
/// connection handling is delegated to <see cref="IConnectionManager"/>.
/// </summary>
public partial class TabViewModel : ViewModelBase
{
    private readonly IConnectionManager _connectionManager;

    [ObservableProperty]
    private ServerProfile _serverProfile;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    /// <summary>
    /// Number of events that have arrived while this tab was not selected.
    /// Hidden (and reset to 0) as soon as the tab becomes selected, then
    /// starts counting again from 0 for whatever arrives afterwards.
    /// </summary>
    [ObservableProperty]
    private int _unreadEventCount;

    /// <summary>
    /// Set by <see cref="MainWindowViewModel"/> whenever this tab becomes the
    /// active/selected one. While selected, newly arriving events no longer
    /// increase <see cref="UnreadEventCount"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private HintFilter _selectedHintFilter = HintFilter.All;

    [ObservableProperty]
    private string _messageToSend = string.Empty;

    public ObservableCollection<EventEntry> Events { get; } = new();

    public ObservableCollection<HintEntry> Hints { get; } = new();

    public bool IsConnected => ConnectionState == ConnectionState.Connected;

    public bool CanConnect => ConnectionState is ConnectionState.Disconnected or ConnectionState.Error;

    public bool CanDisconnect => ConnectionState is ConnectionState.Connected
        or ConnectionState.Connecting
        or ConnectionState.Reconnecting;

    /// <summary>
    /// Hints filtered by <see cref="SelectedHintFilter"/>. There is no
    /// live-filtering collection view available, so this is recomputed on
    /// every read; a property-changed notification is raised manually
    /// whenever <see cref="Hints"/>, a hint's <see cref="HintEntry.Found"/>,
    /// or the filter itself changes, which is enough for the bound ListBox
    /// to re-pull the sequence.
    /// </summary>
    public IEnumerable<HintEntry> VisibleHints =>
        SelectedHintFilter == HintFilter.Unfound ? Hints.Where(h => !h.Found) : Hints;

    public int UnfoundHintCount => Hints.Count(h => !h.Found);

    /// <summary>
    /// Whether the unread-event badge should be shown; derived purely for
    /// XAML visibility bindings (Avalonia has no built-in "greater than
    /// zero" converter).
    /// </summary>
    public bool HasUnreadEvents => UnreadEventCount > 0;

    /// <summary>
    /// Tab header text: profile name, plus the unfound-hint count in
    /// parentheses once there is at least one unfound hint.
    /// </summary>
    public string HeaderText => UnfoundHintCount > 0
        ? $"{ServerProfile.Name} ({UnfoundHintCount})"
        : ServerProfile.Name;

    public TabViewModel(ServerProfile serverProfile, IConnectionManager connectionManager)
    {
        _serverProfile = serverProfile;
        _connectionManager = connectionManager;

        Events.CollectionChanged += OnEventsCollectionChanged;
        Hints.CollectionChanged += OnHintsCollectionChanged;
        ServerProfile.PropertyChanged += OnServerProfilePropertyChanged;
    }

    partial void OnServerProfileChanged(ServerProfile? oldValue, ServerProfile newValue)
    {
        // The profile instance is replaced wholesale when a profile is edited
        // (see MainWindowViewModel.AddOrUpdateProfile); move the subscription
        // over and refresh anything derived from it (e.g. the header text).
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnServerProfilePropertyChanged;
        }

        newValue.PropertyChanged += OnServerProfilePropertyChanged;
        OnPropertyChanged(nameof(HeaderText));
    }

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            // Catching up on this tab hides the badge and resets the count,
            // so the next batch of events received while away starts at 0.
            UnreadEventCount = 0;
        }
    }

    partial void OnUnreadEventCountChanged(int value) => OnPropertyChanged(nameof(HasUnreadEvents));

    partial void OnSelectedHintFilterChanged(HintFilter value) => OnPropertyChanged(nameof(VisibleHints));

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && !IsSelected)
        {
            UnreadEventCount += e.NewItems?.Count ?? 1;
        }
    }

    private void OnHintsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is HintEntry hint)
                {
                    hint.PropertyChanged += OnHintEntryPropertyChanged;
                }
            }
        }

        RaiseHintAggregatesChanged();
    }

    private void OnHintEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HintEntry.Found))
        {
            RaiseHintAggregatesChanged();
        }
    }

    private void OnServerProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerProfile.Name))
        {
            OnPropertyChanged(nameof(HeaderText));
        }
    }

    private void RaiseHintAggregatesChanged()
    {
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(UnfoundHintCount));
        OnPropertyChanged(nameof(HeaderText));
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => _connectionManager.ConnectAsync(this);

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => _connectionManager.DisconnectAsync(this);

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task SendMessageAsync()
    {
        var text = MessageToSend;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageToSend = string.Empty;
        await _connectionManager.SendMessageAsync(this, text);
    }

    [RelayCommand]
    private void ShowAllHints() => SelectedHintFilter = HintFilter.All;

    [RelayCommand]
    private void ShowUnfoundHints() => SelectedHintFilter = HintFilter.Unfound;
}
