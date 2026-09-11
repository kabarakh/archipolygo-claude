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
/// Backs the toggleable Dashboard view (Feature-Plaene/Archiv/Dashboard-Tab.md) -
/// an "all servers at a glance" overview shown instead of (not alongside)
/// <see cref="MainWindowViewModel"/>'s <c>TabControl</c>, see
/// <see cref="MainWindowViewModel.IsDashboardVisible"/>. Holds a live
/// reference to <see cref="MainWindowViewModel.Groups"/> (not a copy) plus a
/// callback to actually select a group and leave the dashboard, so this
/// class stays free of a hard reference to <see cref="MainWindowViewModel"/>
/// itself - same callback-based decoupling as <see cref="ConnectionEditorViewModel"/>.
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly ObservableCollection<GroupViewModel> _groups;
    private readonly Action<GroupViewModel> _selectGroupAndLeaveDashboard;

    /// <summary>Handlers currently subscribed to each group's <see cref="GroupViewModel.Hints"/> collection, so <see cref="UnsubscribeGroup"/> can remove the exact same delegate instance later.</summary>
    private readonly Dictionary<GroupViewModel, NotifyCollectionChangedEventHandler> _hintsCollectionHandlers = new();

    /// <summary>Same as <see cref="_hintsCollectionHandlers"/>, for each group's <see cref="GroupViewModel.Events"/> collection instead.</summary>
    private readonly Dictionary<GroupViewModel, NotifyCollectionChangedEventHandler> _eventsCollectionHandlers = new();

    public ObservableCollection<GroupViewModel> Groups => _groups;

    /// <summary>Server+slot display filter for <see cref="VisibleHints"/> - see <see cref="DashboardServerSlotFilter"/>.</summary>
    public DashboardServerSlotFilter HintFilter { get; }

    /// <summary>Server+slot display filter for <see cref="VisibleEvents"/> - see <see cref="DashboardServerSlotFilter"/>.</summary>
    public DashboardServerSlotFilter EventsFilter { get; }

    public DashboardViewModel(ObservableCollection<GroupViewModel> groups, Action<GroupViewModel> selectGroupAndLeaveDashboard)
    {
        _groups = groups;
        _selectGroupAndLeaveDashboard = selectGroupAndLeaveDashboard;

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

    /// <summary>Either filter's selection changed (server or slot) - re-raise the dependent <c>VisibleX</c> property so bound lists refresh.</summary>
    private void OnFilterPropertyChanged(PropertyChangedEventArgs e, string visiblePropertyName)
    {
        if (e.PropertyName is nameof(DashboardServerSlotFilter.SelectedServer) or nameof(DashboardServerSlotFilter.SelectedSlot))
        {
            OnPropertyChanged(visiblePropertyName);
        }
    }

    /// <summary>
    /// Row click (Overview list, or a row in the shared Hints list below) -
    /// selects the clicked server's tab and switches back to the normal
    /// <c>TabControl</c> view.
    /// </summary>
    public void SelectGroupAndLeaveDashboard(GroupViewModel group) => _selectGroupAndLeaveDashboard(group);

    public int TotalServers => _groups.Count;

    public int TotalUnreadEvents => _groups.Sum(g => g.UnreadEventCount);

    public int TotalUnfoundHints => _groups.Sum(g => g.UnfoundHintCount);

    /// <summary>Header line for <see cref="Views.DashboardView"/> - "N servers · M unread events · K open hints", see the mockup.</summary>
    public string SummaryText => $"{TotalServers} servers · {TotalUnreadEvents} unread events · {TotalUnfoundHints} open hints";

    /// <summary>
    /// Which panel the left column shows - the per-server Overview list, or
    /// the shared Events view (see <see cref="VisibleEvents"/>). A
    /// Button+IsVisible toggle in <see cref="Views.DashboardView"/>, not a
    /// real <c>TabControl</c> - see <see cref="DashboardLeftPanel"/>'s doc
    /// comment for why. Defaults to Overview (dev decision) - the "at a
    /// glance" view is what the Dashboard is for first.
    /// </summary>
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

        // EventEntry is immutable (see its own doc comment) - unlike Hints,
        // no per-entry PropertyChanged subscription is needed, just the
        // collection itself.
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
        if (e.PropertyName is nameof(GroupViewModel.UnreadEventCount) or nameof(GroupViewModel.UnfoundHintCount))
        {
            RaiseAggregatesChanged();
        }

        if (e.PropertyName == nameof(GroupViewModel.IsLeaderConnected) && ReferenceEquals(sender, SelectedSendServerGroup))
        {
            OnPropertyChanged(nameof(CanSendMessage));
        }
    }

    /// <summary>A hint was added/removed on some group's (unfiltered) <see cref="GroupViewModel.Hints"/> - keep this list's own per-entry <c>Found</c> subscriptions and <see cref="VisibleHints"/> current.</summary>
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

    /// <summary>A hint flips <c>Found</c> - unlike a tab's own Hints panel, this view shows only open hints, so the hint simply drops out of <see cref="VisibleHints"/> rather than changing a checkmark.</summary>
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
    // See Feature-Plaene/Archiv/Dashboard-Tab.md's "Geteilte Hints-Übersicht" section.
    // Deliberately reads the raw (unfiltered) GroupViewModel.Hints of every
    // group rather than each tab's own already-filtered VisibleHints - a
    // narrow filter left set on some tab the user forgot about must not
    // silently hide hints here too, and this view's own filter selections
    // must not leak back into that tab's filters either (see the plan's
    // "Warum nebeneinander..." section for the full reasoning).

    /// <summary>
    /// Own, independent instance - never the same one as any tab's own
    /// <see cref="GroupViewModel.SelectedHintItemCategoryFilter"/>, so
    /// changing this filter here never affects (or is affected by) any
    /// individual server tab. The only filter dimension here that isn't
    /// covered by <see cref="HintFilter"/> - Events has no equivalent.
    /// </summary>
    [ObservableProperty]
    private ItemCategoryFilter _selectedHintItemCategoryFilter = ItemCategoryFilter.All;

    /// <summary>
    /// Every configured server's open hints, tagged with their owning group
    /// (see <see cref="DashboardHintRow"/>), narrowed by <see cref="HintFilter"/>'s
    /// server/slot selection and item category. Found/unfound is
    /// deliberately not a filter dimension here - only open hints ever show,
    /// see the plan's "Found/Unfound ist hier bewusst kein Filter" section.
    /// </summary>
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
    // Follow-up to the Hints section above (dev request, 2026-09-11, revised
    // 2026-09-12): the Server+Slot dropdowns up top are a pure display
    // filter, exactly like the Hints ones (VisibleEvents reads the raw,
    // unfiltered GroupViewModel.Events of every group, same "don't leak tab
    // filters in either direction" reasoning as VisibleHints) - picking a
    // slot there never touches any live connection. "Who to send as" is a
    // deliberately separate, unrelated control down by the message box (see
    // SelectedSendServerGroup below) - the dev's explicit call to decouple
    // filtering from "who's chatting".

    /// <summary>
    /// Every configured server's events, tagged with their owning group (see
    /// <see cref="DashboardEventRow"/>), narrowed by <see cref="EventsFilter"/>'s
    /// server/slot selection - exactly the same two-stage filter cascade as
    /// <see cref="VisibleHints"/> (both share <see cref="DashboardServerSlotFilter"/>).
    /// A room-wide entry (<see cref="EventEntry.SlotId"/> null, e.g. plain
    /// chat) always passes the slot filter, same as a tab's own Events list.
    /// </summary>
    public IEnumerable<DashboardEventRow> VisibleEvents
    {
        get
        {
            var groups = EventsFilter.SelectedServer is null
                ? (IEnumerable<GroupViewModel>)_groups
                : new[] { EventsFilter.SelectedServer };

            var rows = groups.SelectMany(g => g.Events.Select(e => new DashboardEventRow(g, e)));

            if (EventsFilter.SelectedSlot is not null)
            {
                var filterId = EventsFilter.SelectedSlot.Id;
                rows = rows.Where(r => r.Event.SlotId is null || r.Event.SlotId == filterId);
            }

            return rows;
        }
    }

    // ── "Who's chatting" - deliberately separate from the filter above ──

    /// <summary>
    /// Which server to send through - lives down by the message box in
    /// Views/DashboardView.axaml, completely independent of
    /// <see cref="SelectedEventsServerFilter"/> (the dev's explicit call:
    /// filtering what you see must not be tangled up with who you're
    /// chatting as). No "All servers" entry - <c>ItemsSource</c> binds
    /// straight to <see cref="Groups"/>, since sending needs one concrete
    /// server or not at all.
    /// </summary>
    [ObservableProperty]
    private GroupViewModel? _selectedSendServerGroup;

    /// <summary>
    /// Whether the message row is usable - a concrete server is picked (via
    /// <see cref="SelectedSendServerGroup"/>) and its leader is actually
    /// connected. A plain nested binding straight to
    /// <c>SelectedSendServerGroup.IsLeaderConnected</c> does not reliably
    /// fall back to <c>false</c> when the intermediate is null - that
    /// silently left "Send" enabled with nothing selected (caught by
    /// DashboardTabTests.EventsPanel_SlotAndSendControls_DisabledUntilAServerIsPicked),
    /// so this is a real, independently-notified property instead (see
    /// <see cref="OnGroupPropertyChanged"/>/<see cref="OnSelectedSendServerGroupChanged"/>
    /// for what keeps it current).
    /// </summary>
    public bool CanSendMessage => SelectedSendServerGroup?.IsLeaderConnected == true;

    /// <summary>
    /// Picking a slot in Views/DashboardView.axaml's send-row Slot dropdown
    /// (bound to <c>SelectedSendServerGroup.Slots</c>/<c>.SelectedChatSlot</c>)
    /// is a real leader switch - the exact same live property/collection the
    /// tab's own "Chat as" dropdown uses, not a Dashboard-local copy, since
    /// both must always agree on the one true current leader.
    /// </summary>
    partial void OnSelectedSendServerGroupChanged(GroupViewModel? value) => OnPropertyChanged(nameof(CanSendMessage));
}
