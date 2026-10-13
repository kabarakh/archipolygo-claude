using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// The "all servers at a glance" Dashboard, shown instead of the tab strip. Takes callbacks
/// rather than a <see cref="MainWindowViewModel"/> reference to stay decoupled from it.
/// </summary>
public partial class DashboardViewModel : ViewModelBase, IEventItemClassFilter
{
    private readonly ObservableCollection<GroupViewModel> _groups;
    private readonly Action<GroupViewModel> _selectGroupAndLeaveDashboard;
    private readonly Action<GroupViewModel, GroupViewModel, bool> _reorderGroups;

    // Kept so UnsubscribeGroup can remove the exact same delegate instances.
    private readonly Dictionary<GroupViewModel, NotifyCollectionChangedEventHandler> _hintsCollectionHandlers = new();

    private readonly Dictionary<GroupViewModel, NotifyCollectionChangedEventHandler> _eventsCollectionHandlers = new();

    public ObservableCollection<GroupViewModel> Groups => _groups;

    public DashboardServerSlotFilter HintFilter { get; }

    public DashboardServerSlotFilter EventsFilter { get; }

    public FilterBarLayoutState FilterLayout { get; }

    public string EventFilterSummaryText => FilterSummaryText.Join(
        FilterSummaryText.Relevance(SelectedEventRelevanceFilter),
        FilterSummaryText.Category(SelectedEventCategoryFilter),
        FilterSummaryText.ItemClasses(this),
        EventsFilter.SelectedServer?.HeaderText,
        FilterSummaryText.Slot(EventsFilter.SelectedSlot));

    public string HintFilterSummaryText => FilterSummaryText.Join(
        HintFilter.SelectedServer?.HeaderText,
        FilterSummaryText.Slot(HintFilter.SelectedSlot),
        FilterSummaryText.ItemCategory(SelectedHintItemCategoryFilter));

    /// <summary>Every filter change already raises VisibleX, so the summary texts piggyback on that.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(VisibleEvents):
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(EventFilterSummaryText)));
                break;
            case nameof(VisibleHints):
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HintFilterSummaryText)));
                break;
        }
    }

    public DashboardViewModel(ObservableCollection<GroupViewModel> groups, Action<GroupViewModel> selectGroupAndLeaveDashboard,
        Action<GroupViewModel, GroupViewModel, bool> reorderGroups, FilterBarLayoutState? filterLayout = null)
    {
        _groups = groups;
        FilterLayout = filterLayout ?? new FilterBarLayoutState();
        _selectGroupAndLeaveDashboard = selectGroupAndLeaveDashboard;
        _reorderGroups = reorderGroups;

        HintFilter = new DashboardServerSlotFilter(_groups);
        HintFilter.PropertyChanged += (_, e) => OnFilterPropertyChanged(e, nameof(VisibleHints));

        EventsFilter = new DashboardServerSlotFilter(_groups);
        EventsFilter.PropertyChanged += (_, e) => OnFilterPropertyChanged(e, nameof(VisibleEvents));

        _groups.CollectionChanged += OnGroupsCollectionChanged;
        foreach (var group in _groups)
        {
            SubscribeGroup(group);
        }
    }

    private void OnFilterPropertyChanged(PropertyChangedEventArgs e, string visiblePropertyName)
    {
        if (e.PropertyName is nameof(DashboardServerSlotFilter.SelectedServer) or nameof(DashboardServerSlotFilter.SelectedSlot))
        {
            OnPropertyChanged(visiblePropertyName);
        }
    }

    public void SelectGroupAndLeaveDashboard(GroupViewModel group) => _selectGroupAndLeaveDashboard(group);

    public void ReorderGroup(GroupViewModel source, GroupViewModel target, bool insertAfter) => _reorderGroups(source, target, insertAfter);

    public int TotalServers => _groups.Count;

    /// <summary>The Dashboard doesn't count as "seen", so it never resets these counts.</summary>
    public int TotalUnreadEvents => _groups.Sum(g => g.UnreadAttentionCount);

    public int TotalUnfoundHints => _groups.Sum(g => g.UnfoundHintCount);

    public string SummaryText => $"{TotalServers} servers · {TotalUnreadEvents} unread events · {TotalUnfoundHints} open hints";

    /// <summary>Button+IsVisible toggle rather than a real <c>TabControl</c>, see <see cref="DashboardLeftPanel"/>.</summary>
    [ObservableProperty]
    private DashboardLeftPanel _selectedLeftPanel = DashboardLeftPanel.Overview;

    [RelayCommand]
    private void ShowOverviewPanel() => SelectedLeftPanel = DashboardLeftPanel.Overview;

    [RelayCommand]
    private void ShowEventsPanel() => SelectedLeftPanel = DashboardLeftPanel.Events;

    private void OnGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GroupViewModel group in e.OldItems)
            {
                UnsubscribeGroup(group);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GroupViewModel group in e.NewItems)
            {
                SubscribeGroup(group);
            }
        }

        HintFilter.RebuildServerOptions();
        EventsFilter.RebuildServerOptions();
        RaiseAggregatesChanged();
        OnPropertyChanged(nameof(HasAdminServer));
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(VisibleEvents));
    }

    private void SubscribeGroup(GroupViewModel group)
    {
        group.PropertyChanged += OnGroupPropertyChanged;

        NotifyCollectionChangedEventHandler hintsHandler = (_, e) => OnGroupHintsCollectionChanged(e);
        _hintsCollectionHandlers[group] = hintsHandler;
        group.Hints.CollectionChanged += hintsHandler;

        foreach (var hint in group.Hints)
        {
            hint.PropertyChanged += OnHintEntryPropertyChanged;
        }

        // EventEntry is immutable, so unlike hints no per-entry subscription is needed.
        NotifyCollectionChangedEventHandler eventsHandler = (_, _) => OnPropertyChanged(nameof(VisibleEvents));
        _eventsCollectionHandlers[group] = eventsHandler;
        group.Events.CollectionChanged += eventsHandler;
    }

    private void UnsubscribeGroup(GroupViewModel group)
    {
        group.PropertyChanged -= OnGroupPropertyChanged;

        if (_hintsCollectionHandlers.Remove(group, out var hintsHandler))
        {
            group.Hints.CollectionChanged -= hintsHandler;
        }

        foreach (var hint in group.Hints)
        {
            hint.PropertyChanged -= OnHintEntryPropertyChanged;
        }

        if (_eventsCollectionHandlers.Remove(group, out var eventsHandler))
        {
            group.Events.CollectionChanged -= eventsHandler;
        }

        HintFilter.HandleGroupRemoved(group);
        EventsFilter.HandleGroupRemoved(group);

        if (SelectedSendServerGroup == group)
        {
            SelectedSendServerGroup = null;
        }
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroupViewModel.UnreadAttentionCount) or nameof(GroupViewModel.UnfoundHintCount))
        {
            RaiseAggregatesChanged();
        }

        if (e.PropertyName == nameof(GroupViewModel.ShowAdminButton))
        {
            OnPropertyChanged(nameof(HasAdminServer));
        }

        if (e.PropertyName == nameof(GroupViewModel.IsLeaderConnected) && ReferenceEquals(sender, SelectedSendServerGroup))
        {
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    private void OnGroupHintsCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (HintEntry hint in e.OldItems)
            {
                hint.PropertyChanged -= OnHintEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (HintEntry hint in e.NewItems)
            {
                hint.PropertyChanged += OnHintEntryPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(VisibleHints));
    }

    /// <summary>Only open hints show here, so a hint flipping to found simply drops out of <see cref="VisibleHints"/>.</summary>
    private void OnHintEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HintEntry.Found))
        {
            OnPropertyChanged(nameof(VisibleHints));
        }
    }

    private void RaiseAggregatesChanged()
    {
        OnPropertyChanged(nameof(TotalServers));
        OnPropertyChanged(nameof(TotalUnreadEvents));
        OnPropertyChanged(nameof(TotalUnfoundHints));
        OnPropertyChanged(nameof(SummaryText));
    }

    // ── Shared, server-spanning Hints overview ──
    // Deliberately reads each group's raw, unfiltered Hints rather than its VisibleHints: a filter
    // forgotten on some tab must not hide hints here, and this view's filters must not leak into tabs.

    /// <summary>Independent of every tab's own filter instance, so neither affects the other.</summary>
    [ObservableProperty]
    private ItemCategoryFilter _selectedHintItemCategoryFilter = ItemCategoryFilter.All;

    /// <summary>Open hints only - found/unfound is deliberately not a filter dimension here.</summary>
    public IEnumerable<DashboardHintRow> VisibleHints
    {
        get
        {
            var groups = HintFilter.SelectedServer is null
                ? (IEnumerable<GroupViewModel>)_groups
                : new[] { HintFilter.SelectedServer };

            var rows = groups.SelectMany(g => g.Hints.Select(h => new DashboardHintRow(g, h)))
                             .Where(r => !r.Hint.Found);

            rows = SelectedHintItemCategoryFilter switch
            {
                ItemCategoryFilter.Progress => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemProgression),
                ItemCategoryFilter.Useful   => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemUseful),
                ItemCategoryFilter.Normal   => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemOther),
                ItemCategoryFilter.Trap     => rows.Where(r => r.Hint.ItemKind == EventTextSegmentKind.ItemTrap),
                _                           => rows,
            };

            if (HintFilter.SelectedSlot is not null)
                rows = rows.Where(r => r.Hint.SlotId == HintFilter.SelectedSlot.Id);

            return rows;
        }
    }

    partial void OnSelectedHintItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleHints));

    [RelayCommand]
    private void ShowAllHintItemCategories() => SelectedHintItemCategoryFilter = ItemCategoryFilter.All;

    [RelayCommand]
    private void ShowProgressHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Progress;

    [RelayCommand]
    private void ShowUsefulHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Useful;

    [RelayCommand]
    private void ShowNormalHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Normal;

    [RelayCommand]
    private void ShowTrapHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Trap;

    // ── Shared, server-spanning Events view ──
    // Same raw-collection, no-leaking reasoning as the Hints section. The Server+Slot filter is
    // display-only; who to send as is a deliberately separate control (SelectedSendServerGroup).

    /// <summary>Independent of every tab's own filter instance, so neither affects the other.</summary>
    [ObservableProperty]
    private EventRelevanceFilter _selectedEventRelevanceFilter = EventRelevanceFilter.All;

    [ObservableProperty]
    private EventCategoryFilter _selectedEventCategoryFilter = EventCategoryFilter.All;

    [ObservableProperty]
    private bool _showProgressionItemEvents = true;

    [ObservableProperty]
    private bool _showUsefulItemEvents = true;

    [ObservableProperty]
    private bool _showFillerItemEvents = true;

    [ObservableProperty]
    private bool _showTrapItemEvents = true;

    partial void OnSelectedEventRelevanceFilterChanged(EventRelevanceFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnSelectedEventCategoryFilterChanged(EventCategoryFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnShowProgressionItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowUsefulItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowFillerItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowTrapItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    private void OnEventItemClassFilterChanged()
    {
        OnPropertyChanged(nameof(VisibleEvents));
        OnPropertyChanged(nameof(EventItemClassFilterButtonText));
        OnPropertyChanged(nameof(IsEventItemClassFilterActive));
    }

    /// <inheritdoc/>
    public string EventItemClassFilterButtonText => EventItemClassFilterText.ButtonText(this);

    /// <inheritdoc/>
    public bool IsEventItemClassFilterActive => EventItemClassFilterText.IsActive(this);

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

    [RelayCommand]
    private void ShowChatEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Chat;

    [RelayCommand]
    private void ShowAdminEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Admin;

    public bool HasAdminServer => _groups.Any(g => g.ShowAdminButton);

    /// <summary>Room-wide entries (<see cref="EventEntry.SlotId"/> null, e.g. plain chat) always pass the slot filter.</summary>
    public IEnumerable<DashboardEventRow> VisibleEvents
    {
        get
        {
            var groups = EventsFilter.SelectedServer is null
                ? (IEnumerable<GroupViewModel>)_groups
                : new[] { EventsFilter.SelectedServer };

            var rows = groups.SelectMany(g => g.Events.Select(e => new DashboardEventRow(g, e)));

            if (SelectedEventRelevanceFilter == EventRelevanceFilter.ConcernsMe)
            {
                rows = rows.Where(r => r.Event.ConcernsOwnSlot);
            }

            rows = SelectedEventCategoryFilter switch
            {
                EventCategoryFilter.Hints => rows.Where(r => r.Event.Type == EventType.HintReceived),
                EventCategoryFilter.Items => rows.Where(r => r.Event.Type == EventType.ItemReceived),
                EventCategoryFilter.Chat => rows.Where(r => r.Event.Type == EventType.Chat),
                EventCategoryFilter.Admin => rows.Where(r => r.Event.Type == EventType.Admin),
                _ => rows,
            };

            rows = rows.Where(r => r.Event.ItemKind switch
            {
                EventTextSegmentKind.ItemProgression => ShowProgressionItemEvents,
                EventTextSegmentKind.ItemUseful => ShowUsefulItemEvents,
                EventTextSegmentKind.ItemOther => ShowFillerItemEvents,
                EventTextSegmentKind.ItemTrap => ShowTrapItemEvents,
                _ => true,
            });

            if (EventsFilter.SelectedSlot is not null)
            {
                var filterId = EventsFilter.SelectedSlot.Id;
                rows = rows.Where(r => r.Event.SlotId is null || r.Event.SlotId == filterId);
            }

            // SelectMany yields server by server; sort so the result is a true cross-server merge.
            return rows.OrderBy(r => r.Event.Timestamp);
        }
    }

    // ── "Who's chatting" - deliberately separate from the filter above ──

    /// <summary>No "All servers" entry: sending needs one concrete server.</summary>
    [ObservableProperty]
    private GroupViewModel? _selectedSendServerGroup;

    /// <summary>
    /// A nested binding to <c>SelectedSendServerGroup.IsLeaderConnected</c> didn't reliably fall back
    /// to false when the group was null (left "Send" enabled), hence this separately notified property.
    /// </summary>
    public bool CanSendMessage => SelectedSendServerGroup?.IsLeaderConnected == true;

    /// <summary>The send-row Slot dropdown binds to the group's live <c>SelectedChatSlot</c>, so picking one is a real leader switch.</summary>
    partial void OnSelectedSendServerGroupChanged(GroupViewModel? value) => OnPropertyChanged(nameof(CanSendMessage));
}
