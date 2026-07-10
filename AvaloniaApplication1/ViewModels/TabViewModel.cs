using System;
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
    private HintFilter _selectedHintFilter = HintFilter.Unfound;

    [ObservableProperty]
    private HintRoleFilter _selectedHintRoleFilter = HintRoleFilter.All;

    [ObservableProperty]
    private EventRelevanceFilter _selectedEventRelevanceFilter = EventRelevanceFilter.All;

    [ObservableProperty]
    private EventCategoryFilter _selectedEventCategoryFilter = EventCategoryFilter.All;

    [ObservableProperty]
    private RightPanelView _selectedRightPanel = RightPanelView.Hints;

    [ObservableProperty]
    private ItemCategoryFilter _selectedItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    [ObservableProperty]
    private string _messageToSend = string.Empty;

    public ObservableCollection<EventEntry> Events { get; } = new();

    public ObservableCollection<HintEntry> Hints { get; } = new();

    public ObservableCollection<ReceivedItemEntry> ReceivedItems { get; } = new();

    public bool IsConnected => ConnectionState == ConnectionState.Connected;

    public bool CanConnect => ConnectionState is ConnectionState.Disconnected or ConnectionState.Error;

    public bool CanDisconnect => ConnectionState is ConnectionState.Connected
        or ConnectionState.Queued
        or ConnectionState.Connecting
        or ConnectionState.Reconnecting;

    /// <summary>
    /// Hints filtered by <see cref="SelectedHintFilter"/> and
    /// <see cref="SelectedHintRoleFilter"/>. The two dimensions combine
    /// independently (e.g. "Unfound + I find" shows only unfound hints at
    /// this slot's locations). A hint where finder and receiver are the same
    /// slot appears under both role filters, as expected.
    /// </summary>
    public IEnumerable<HintEntry> VisibleHints
    {
        get
        {
            IEnumerable<HintEntry> hints = Hints;

            if (SelectedHintFilter == HintFilter.Unfound)
                hints = hints.Where(h => !h.Found);

            hints = SelectedHintRoleFilter switch
            {
                HintRoleFilter.IFind    => hints.Where(h => h.FindingPlayerKind == EventTextSegmentKind.OwnSlotName),
                HintRoleFilter.IReceive => hints.Where(h => h.ReceivingPlayerKind == EventTextSegmentKind.OwnSlotName),
                _                       => hints,
            };

            if (!string.IsNullOrEmpty(ItemSearchText))
                hints = hints.Where(h => h.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));

            return hints;
        }
    }

    public int UnfoundHintCount => Hints.Count(h => !h.Found);

    /// <summary>
    /// Number of unfound hints where this slot is the finder ("My location").
    /// A hint where finder == receiver contributes to both this count and
    /// <see cref="UnfoundHintIReceiveCount"/>, so their sum can exceed
    /// <see cref="UnfoundHintCount"/>.
    /// </summary>
    public int UnfoundHintIFindCount =>
        Hints.Count(h => !h.Found && h.FindingPlayerKind == EventTextSegmentKind.OwnSlotName);

    /// <summary>
    /// Number of unfound hints where this slot receives the item ("My item").
    /// </summary>
    public int UnfoundHintIReceiveCount =>
        Hints.Count(h => !h.Found && h.ReceivingPlayerKind == EventTextSegmentKind.OwnSlotName);

    /// <summary>Button label for the Hints panel selector: "Hints (N)" when N &gt; 0.</summary>
    public string HintsPanelButtonText => UnfoundHintCount > 0
        ? $"Hints ({UnfoundHintCount})"
        : "Hints";

    /// <summary>Button label for the "My location" role filter.</summary>
    public string HintsIFindButtonText => UnfoundHintIFindCount > 0
        ? $"My location ({UnfoundHintIFindCount})"
        : "My location";

    /// <summary>Button label for the "My item" role filter.</summary>
    public string HintsIReceiveButtonText => UnfoundHintIReceiveCount > 0
        ? $"My item ({UnfoundHintIReceiveCount})"
        : "My item";

    /// <summary>
    /// <see cref="Events"/> filtered by <see cref="SelectedEventRelevanceFilter"/>
    /// and <see cref="SelectedEventCategoryFilter"/>. The two filters combine
    /// (e.g. "concerns me" + "hints" shows only hints relevant to this slot).
    /// <see cref="Events"/> itself is never modified, so clearing either
    /// filter (setting it back to "All") makes everything reappear. As with
    /// <see cref="VisibleHints"/>, there is no live-filtering collection view
    /// available, so this is recomputed on every read and a property-changed
    /// notification is raised manually whenever <see cref="Events"/> or either
    /// filter changes.
    /// </summary>
    public IEnumerable<EventEntry> VisibleEvents
    {
        get
        {
            IEnumerable<EventEntry> events = Events;

            if (SelectedEventRelevanceFilter == EventRelevanceFilter.ConcernsMe)
            {
                events = events.Where(e => e.ConcernsOwnSlot);
            }

            events = SelectedEventCategoryFilter switch
            {
                EventCategoryFilter.Hints => events.Where(e => e.Type == EventType.HintReceived),
                EventCategoryFilter.Items => events.Where(e => e.Type == EventType.ItemReceived),
                _ => events,
            };

            return events;
        }
    }

    /// <summary>
    /// <see cref="ReceivedItems"/> filtered by
    /// <see cref="SelectedItemCategoryFilter"/> and <see cref="ItemSearchText"/>.
    /// The two dimensions combine independently. Recomputed on every read;
    /// property-changed notifications are raised via the callbacks below.
    /// </summary>
    public IEnumerable<ReceivedItemEntry> VisibleReceivedItems
    {
        get
        {
            IEnumerable<ReceivedItemEntry> items = ReceivedItems;

            items = SelectedItemCategoryFilter switch
            {
                ItemCategoryFilter.Progress => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemProgression),
                ItemCategoryFilter.Useful   => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemUseful),
                ItemCategoryFilter.Normal   => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemOther),
                ItemCategoryFilter.Trap     => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemTrap),
                _                           => items,
            };

            if (!string.IsNullOrEmpty(ItemSearchText))
            {
                items = items.Where(i =>
                    i.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));
            }

            return items;
        }
    }

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
    public string HeaderText => ServerProfile.Name;

    public TabViewModel(ServerProfile serverProfile, IConnectionManager connectionManager)
    {
        _serverProfile = serverProfile;
        _connectionManager = connectionManager;

        Events.CollectionChanged += OnEventsCollectionChanged;
        Hints.CollectionChanged += OnHintsCollectionChanged;
        ReceivedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(VisibleReceivedItems));
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

    partial void OnSelectedHintRoleFilterChanged(HintRoleFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleReceivedItems));

    partial void OnItemSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(VisibleReceivedItems));
    }

    partial void OnSelectedEventRelevanceFilterChanged(EventRelevanceFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnSelectedEventCategoryFilterChanged(EventCategoryFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Events is the unfiltered source; VisibleEvents is a derived
        // sequence with no observable-collection semantics of its own, so it
        // needs an explicit nudge whenever the source changes (new entries,
        // resets, ...), regardless of whether this tab is currently selected.
        OnPropertyChanged(nameof(VisibleEvents));

        if (e.Action != NotifyCollectionChangedAction.Add || IsSelected || e.NewItems is null)
        {
            return;
        }

        // Only events that actually concern this slot (its own items, hints
        // for/from it, chat naming it, its own connect/disconnect/errors)
        // should bump the unread badge - not e.g. banter between two other
        // players in the same multiworld.
        var relevantCount = 0;
        foreach (var item in e.NewItems)
        {
            if (item is EventEntry { ConcernsOwnSlot: true })
            {
                relevantCount++;
            }
        }

        UnreadEventCount += relevantCount;
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
        OnPropertyChanged(nameof(UnfoundHintIFindCount));
        OnPropertyChanged(nameof(UnfoundHintIReceiveCount));
        OnPropertyChanged(nameof(HintsPanelButtonText));
        OnPropertyChanged(nameof(HintsIFindButtonText));
        OnPropertyChanged(nameof(HintsIReceiveButtonText));
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

    [RelayCommand]
    private void ShowHintsPanel() => SelectedRightPanel = RightPanelView.Hints;

    [RelayCommand]
    private void ShowReceivedItemsPanel() => SelectedRightPanel = RightPanelView.ReceivedItems;

    [RelayCommand]
    private void ShowAllItemCategories() => SelectedItemCategoryFilter = ItemCategoryFilter.All;

    [RelayCommand]
    private void ShowProgressItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Progress;

    [RelayCommand]
    private void ShowUsefulItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Useful;

    [RelayCommand]
    private void ShowNormalItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Normal;

    [RelayCommand]
    private void ShowTrapItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Trap;

    [RelayCommand]
    private void ShowAllHintRoles() => SelectedHintRoleFilter = HintRoleFilter.All;

    [RelayCommand]
    private void ShowHintsIFind() => SelectedHintRoleFilter = HintRoleFilter.IFind;

    [RelayCommand]
    private void ShowHintsIReceive() => SelectedHintRoleFilter = HintRoleFilter.IReceive;

    [RelayCommand]
    private void ShowAllEventsRelevance() => SelectedEventRelevanceFilter = EventRelevanceFilter.All;

    [RelayCommand]
    private void ShowOwnEventsOnly() => SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;

    [RelayCommand]
    private void ShowAllEventCategories() => SelectedEventCategoryFilter = EventCategoryFilter.All;

    [RelayCommand]
    private void ShowHintEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Hints;

    [RelayCommand]
    private void ShowItemEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Items;
}
