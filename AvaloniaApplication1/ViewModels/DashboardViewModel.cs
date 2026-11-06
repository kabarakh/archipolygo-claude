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
        FilterSummaryText.HideGoaled(HideGoaledEvents),
        EventsFilter.SelectedServer?.HeaderText,
        FilterSummaryText.Slot(EventsFilter.SelectedSlot));

    public string HintFilterSummaryText => FilterSummaryText.Join(
        HintFilter.SelectedServer?.HeaderText,
        FilterSummaryText.Slot(HintFilter.SelectedSlot),
        FilterSummaryText.ItemCategory(SelectedHintItemCategoryFilter),
        FilterSummaryText.HideGoaled(HideGoaledHints));

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
            RefreshVisible(visiblePropertyName);
        }
    }

    // ── Incremental lists ──
    // Stable instances that follow each group's changes, instead of a fresh LINQ result per line: that made the
    // bound ListBox re-read (and the Events list re-sort) everything once per incoming line.
    // See Umsetzungsplan.md, "UI-Updates in Zeitscheiben".

    private readonly RangeObservableCollection<DashboardEventRow> _visibleEvents = new();
    private readonly RangeObservableCollection<DashboardHintRow> _visibleHints = new();

    // Reused rows, so a rebuild hands the ListBox the same row instances.
    private readonly Dictionary<EventEntry, DashboardEventRow> _eventRows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<HintEntry, DashboardHintRow> _hintRows = new(ReferenceEqualityComparer.Instance);

    private bool _visibleEventsStale = true;
    private bool _visibleHintsStale = true;

    private bool _isShown = true;

    /// <summary>
    /// False while the tab view replaces the Dashboard: changes then only mark the lists stale, and showing it
    /// again rebuilds each once. True by default, so a Dashboard built on its own (tests) is always current.
    /// </summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown == value)
            {
                return;
            }

            _isShown = value;
            if (value)
            {
                RefreshVisible(nameof(VisibleEvents));
                RefreshVisible(nameof(VisibleHints));
            }
        }
    }

    /// <summary>Also raises the property: the summary texts piggyback on it, and the binding ignores an unchanged instance.</summary>
    private void RefreshVisible(string visiblePropertyName)
    {
        if (visiblePropertyName == nameof(VisibleEvents))
        {
            _visibleEventsStale = true;
        }
        else
        {
            _visibleHintsStale = true;
        }

        if (!IsShown)
        {
            return;
        }

        // The getter rebuilds; reading it here keeps the list current even with no binding attached.
        _ = visiblePropertyName == nameof(VisibleEvents) ? VisibleEvents : (object)VisibleHints;
        OnPropertyChanged(visiblePropertyName);
    }

    private void EnsureVisibleEventsCurrent()
    {
        if (!_visibleEventsStale)
        {
            return;
        }

        _visibleEventsStale = false;
        _visibleEvents.ResetTo(FilteredGroups(EventsFilter)
            .SelectMany(g => g.Events.Select(e => EventRow(g, e)))
            .Where(PassesEventFilters)
            // SelectMany yields server by server; sort so the result is a true cross-server merge.
            .OrderBy(r => r.Event.Timestamp)
            .ToList());
    }

    private void EnsureVisibleHintsCurrent()
    {
        if (!_visibleHintsStale)
        {
            return;
        }

        _visibleHintsStale = false;
        _visibleHints.ResetTo(FilteredGroups(HintFilter)
            .SelectMany(g => g.Hints.Select(h => HintRow(g, h)))
            .Where(PassesHintFilters)
            .ToList());
    }

    private IEnumerable<GroupViewModel> FilteredGroups(DashboardServerSlotFilter filter) =>
        filter.SelectedServer is null ? _groups : new[] { filter.SelectedServer };

    private DashboardEventRow EventRow(GroupViewModel group, EventEntry entry)
    {
        if (!_eventRows.TryGetValue(entry, out var row))
        {
            row = new DashboardEventRow(group, entry);
            _eventRows[entry] = row;
        }

        return row;
    }

    private DashboardHintRow HintRow(GroupViewModel group, HintEntry hint)
    {
        if (!_hintRows.TryGetValue(hint, out var row))
        {
            row = new DashboardHintRow(group, hint);
            _hintRows[hint] = row;
        }

        return row;
    }

    /// <summary>
    /// An append adds its rows at their timestamp position (almost always the end); a trim at a group's front
    /// removes exactly those rows. Anything else rebuilds.
    /// </summary>
    private void OnGroupEventsCollectionChanged(GroupViewModel group, NotifyCollectionChangedEventArgs e)
    {
        if (!IsShown)
        {
            _visibleEventsStale = true;
            return;
        }

        if (_visibleEventsStale)
        {
            RefreshVisible(nameof(VisibleEvents));
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                if (EventsFilter.SelectedServer is null || EventsFilter.SelectedServer == group)
                {
                    InsertEventRowsByTimestamp(e.NewItems.Cast<EventEntry>().Select(entry => EventRow(group, entry)).Where(PassesEventFilters).ToList());
                }

                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                RemoveEventRows(e.OldItems.Cast<EventEntry>());
                break;
            default:
                RefreshVisible(nameof(VisibleEvents));
                break;
        }
    }

    private void InsertEventRowsByTimestamp(IReadOnlyList<DashboardEventRow> rows)
    {
        var appendAtEnd = new List<DashboardEventRow>();
        foreach (var row in rows)
        {
            var last = appendAtEnd.Count > 0 ? appendAtEnd[^1] : _visibleEvents.Count > 0 ? _visibleEvents[^1] : null;
            if (last is null || row.Event.Timestamp >= last.Event.Timestamp)
            {
                appendAtEnd.Add(row);
                continue;
            }

            _visibleEvents.AddRange(appendAtEnd);
            appendAtEnd.Clear();
            _visibleEvents.Insert(UpperBoundByTimestamp(row.Event.Timestamp), row);
        }

        _visibleEvents.AddRange(appendAtEnd);
    }

    private int UpperBoundByTimestamp(DateTimeOffset timestamp)
    {
        int low = 0, high = _visibleEvents.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (_visibleEvents[mid].Event.Timestamp <= timestamp)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void RemoveEventRows(IEnumerable<EventEntry> removedEntries)
    {
        var removedRows = new HashSet<DashboardEventRow>(ReferenceEqualityComparer.Instance);
        foreach (var entry in removedEntries)
        {
            if (_eventRows.Remove(entry, out var row))
            {
                removedRows.Add(row);
            }
        }

        var leading = 0;
        while (leading < _visibleEvents.Count && removedRows.Contains(_visibleEvents[leading]))
        {
            leading++;
        }

        _visibleEvents.RemoveRange(0, leading);
        if (_visibleEvents.Any(removedRows.Contains))
        {
            // Another server's older lines sit in front of the trimmed ones: one filtering pass, no re-sort.
            _visibleEvents.ResetTo(_visibleEvents.Where(r => !removedRows.Contains(r)).ToList());
        }
    }

    /// <summary>Rows stay grouped by server in tab order, so a new hint goes after its server's last row.</summary>
    private void OnGroupHintsChangedForVisibleHints(GroupViewModel group, NotifyCollectionChangedEventArgs e)
    {
        if (!IsShown)
        {
            _visibleHintsStale = true;
            return;
        }

        if (_visibleHintsStale)
        {
            RefreshVisible(nameof(VisibleHints));
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null ||
            e.NewStartingIndex != group.Hints.Count - e.NewItems.Count)
        {
            RefreshVisible(nameof(VisibleHints));
            return;
        }

        if (HintFilter.SelectedServer is not null && HintFilter.SelectedServer != group)
        {
            return;
        }

        var groupOrder = _groups.IndexOf(group);
        var insertAt = _visibleHints.Count;
        while (insertAt > 0 && _groups.IndexOf(_visibleHints[insertAt - 1].Group) > groupOrder)
        {
            insertAt--;
        }

        foreach (var row in e.NewItems.Cast<HintEntry>().Select(hint => HintRow(group, hint)).Where(PassesHintFilters).ToList())
        {
            _visibleHints.Insert(insertAt++, row);
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
        RefreshVisible(nameof(VisibleHints));
        RefreshVisible(nameof(VisibleEvents));
    }

    private void SubscribeGroup(GroupViewModel group)
    {
        group.PropertyChanged += OnGroupPropertyChanged;

        NotifyCollectionChangedEventHandler hintsHandler = (_, e) => OnGroupHintsCollectionChanged(group, e);
        _hintsCollectionHandlers[group] = hintsHandler;
        group.Hints.CollectionChanged += hintsHandler;

        foreach (var hint in group.Hints)
        {
            hint.PropertyChanged += OnHintEntryPropertyChanged;
        }

        // EventEntry is immutable, so unlike hints no per-entry subscription is needed.
        NotifyCollectionChangedEventHandler eventsHandler = (_, e) => OnGroupEventsCollectionChanged(group, e);
        _eventsCollectionHandlers[group] = eventsHandler;
        group.Events.CollectionChanged += eventsHandler;

        group.GoalStatesChanged += OnGroupGoalStatesChanged;
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

        group.GoalStatesChanged -= OnGroupGoalStatesChanged;

        HintFilter.HandleGroupRemoved(group);
        EventsFilter.HandleGroupRemoved(group);

        if (SelectedSendServerGroup == group)
        {
            SelectedSendServerGroup = null;
        }
    }

    private void OnGroupGoalStatesChanged()
    {
        if (HideGoaledEvents)
        {
            RefreshVisible(nameof(VisibleEvents));
        }

        if (HideGoaledHints)
        {
            RefreshVisible(nameof(VisibleHints));
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

    private void OnGroupHintsCollectionChanged(GroupViewModel group, NotifyCollectionChangedEventArgs e)
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

        if (e.OldItems is not null)
        {
            foreach (HintEntry hint in e.OldItems)
            {
                _hintRows.Remove(hint);
            }
        }

        OnGroupHintsChangedForVisibleHints(group, e);
    }

    /// <summary>Only open hints show here, so a hint flipping to found simply drops out of <see cref="VisibleHints"/>.</summary>
    private void OnHintEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HintEntry.Found) || sender is not HintEntry hint)
        {
            return;
        }

        if (IsShown && !_visibleHintsStale && hint.Found && _hintRows.TryGetValue(hint, out var row))
        {
            var index = _visibleHints.IndexOf(row);
            if (index >= 0)
            {
                _visibleHints.RemoveAt(index);
                OnPropertyChanged(nameof(VisibleHints));
            }

            return;
        }

        RefreshVisible(nameof(VisibleHints));
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
            EnsureVisibleHintsCurrent();
            return _visibleHints;
        }
    }

    private bool PassesHintFilters(DashboardHintRow row)
    {
        var hint = row.Hint;
        var passesCategory = SelectedHintItemCategoryFilter switch
        {
            ItemCategoryFilter.Progress => hint.ItemKind == EventTextSegmentKind.ItemProgression,
            ItemCategoryFilter.Useful   => hint.ItemKind == EventTextSegmentKind.ItemUseful,
            ItemCategoryFilter.Normal   => hint.ItemKind == EventTextSegmentKind.ItemOther,
            ItemCategoryFilter.Trap     => hint.ItemKind == EventTextSegmentKind.ItemTrap,
            _                           => true,
        };

        return !hint.Found
               && passesCategory
               && !(HideGoaledHints && hint.ReceivingSlot?.HasGoal == true)
               && (HintFilter.SelectedSlot is null || hint.SlotId == HintFilter.SelectedSlot.Id);
    }

    partial void OnSelectedHintItemCategoryFilterChanged(ItemCategoryFilter value) => RefreshVisible(nameof(VisibleHints));

    /// <summary>Independent of every tab's own "Hide goaled" switch.</summary>
    [ObservableProperty]
    private bool _hideGoaledHints;

    partial void OnHideGoaledHintsChanged(bool value) => RefreshVisible(nameof(VisibleHints));

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

    [ObservableProperty]
    private bool _hideGoaledEvents;

    partial void OnHideGoaledEventsChanged(bool value) => RefreshVisible(nameof(VisibleEvents));

    partial void OnSelectedEventRelevanceFilterChanged(EventRelevanceFilter value) => RefreshVisible(nameof(VisibleEvents));

    partial void OnSelectedEventCategoryFilterChanged(EventCategoryFilter value) => RefreshVisible(nameof(VisibleEvents));

    partial void OnShowProgressionItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowUsefulItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowFillerItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowTrapItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    private void OnEventItemClassFilterChanged()
    {
        RefreshVisible(nameof(VisibleEvents));
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

    /// <summary>Slot filtering follows <see cref="EventEntry.PassesSlotFilter"/>.</summary>
    public IEnumerable<DashboardEventRow> VisibleEvents
    {
        get
        {
            EnsureVisibleEventsCurrent();
            return _visibleEvents;
        }
    }

    private bool PassesEventFilters(DashboardEventRow row)
    {
        var entry = row.Event;
        var passesCategory = SelectedEventCategoryFilter switch
        {
            EventCategoryFilter.Hints => entry.Type == EventType.HintReceived,
            EventCategoryFilter.Items => entry.Type == EventType.ItemReceived,
            EventCategoryFilter.Chat => entry.Type == EventType.Chat,
            EventCategoryFilter.Admin => entry.Type == EventType.Admin,
            _ => true,
        };

        var passesItemClass = entry.ItemKind switch
        {
            EventTextSegmentKind.ItemProgression => ShowProgressionItemEvents,
            EventTextSegmentKind.ItemUseful => ShowUsefulItemEvents,
            EventTextSegmentKind.ItemOther => ShowFillerItemEvents,
            EventTextSegmentKind.ItemTrap => ShowTrapItemEvents,
            _ => true,
        };

        return (SelectedEventRelevanceFilter != EventRelevanceFilter.ConcernsMe || entry.ConcernsOwnSlot)
               && passesCategory && passesItemClass
               && !(HideGoaledEvents && entry.Receiver?.HasGoal == true)
               && (EventsFilter.SelectedSlot is null || entry.PassesSlotFilter(EventsFilter.SelectedSlot.Id));
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
