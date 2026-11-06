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

/// <summary>The Events/Hints/Items panels: contents, filters, counters and the right column's panel switch.</summary>
public partial class GroupViewModel
{
    /// <summary>Per-panel slot filters, independent of each other; null = all slots.</summary>
    [ObservableProperty]
    private SlotProfile? _selectedEventsSlotFilter;

    [ObservableProperty]
    private SlotProfile? _selectedHintsSlotFilter;

    [ObservableProperty]
    private SlotProfile? _selectedItemsSlotFilter;

    [ObservableProperty]
    private HintFilter _selectedHintFilter = HintFilter.Unfound;

    [ObservableProperty]
    private HintRoleFilter _selectedHintRoleFilter = HintRoleFilter.All;

    /// <summary>Hints' own item-category filter, independent of <see cref="SelectedItemCategoryFilter"/>.</summary>
    [ObservableProperty]
    private ItemCategoryFilter _selectedHintItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private EventRelevanceFilter _selectedEventRelevanceFilter = EventRelevanceFilter.All;

    [ObservableProperty]
    private EventCategoryFilter _selectedEventCategoryFilter = EventCategoryFilter.All;

    /// <summary>
    /// Item-class checkboxes for the Events list; combine with <see cref="SelectedEventCategoryFilter"/>
    /// and also hide hint entries. Entries without an item class always pass.
    /// </summary>
    [ObservableProperty]
    private bool _showProgressionItemEvents = true;

    [ObservableProperty]
    private bool _showUsefulItemEvents = true;

    [ObservableProperty]
    private bool _showFillerItemEvents = true;

    [ObservableProperty]
    private bool _showTrapItemEvents = true;

    /// <summary>Hides entries whose receiver reached their goal; see <see cref="RoomSlotStatus"/>.</summary>
    [ObservableProperty]
    private bool _hideGoaledEvents;

    [ObservableProperty]
    private bool _hideGoaledHints;

    [ObservableProperty]
    private RightPanelView _selectedRightPanel = RightPanelView.Hints;

    [ObservableProperty]
    private ItemCategoryFilter _selectedItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    /// <summary>Append through <see cref="AppendEvent"/>, so an update batch can merge the appends and the trim.</summary>
    public RangeObservableCollection<EventEntry> Events { get; } = new();

    public RangeObservableCollection<HintEntry> Hints { get; } = new();

    public RangeObservableCollection<ReceivedItemEntry> ReceivedItems { get; } = new();

    // Stable instances: replacing them per change made the bound ListBox re-read the whole list per line.
    private FilteredCollection<EventEntry> _visibleEvents = null!;
    private FilteredCollection<HintEntry> _visibleHints = null!;
    private FilteredCollection<ReceivedItemEntry> _visibleReceivedItems = null!;

    private void InitializeVisibleCollections()
    {
        _visibleEvents = new FilteredCollection<EventEntry>(Events, PassesEventFilters);
        _visibleHints = new FilteredCollection<HintEntry>(Hints, PassesHintFilters);
        _visibleReceivedItems = new FilteredCollection<ReceivedItemEntry>(ReceivedItems, PassesReceivedItemFilters);
    }

    // ── Update batch: one list notification per UiUpdateQueue time slice ──

    private int _updateBatchDepth;
    private readonly List<EventEntry> _pendingEvents = new();
    private int _pendingEventHistoryLimit = int.MaxValue;
    private bool _hintAggregatesDirty;
    private bool _visibleHintsStale;

    /// <summary>Called by <see cref="Services.UiUpdateQueue"/> around a time slice; nests.</summary>
    public void BeginUpdateBatch() => _updateBatchDepth++;

    public void EndUpdateBatch()
    {
        if (--_updateBatchDepth > 0)
        {
            return;
        }

        FlushPendingEvents();

        if (_hintAggregatesDirty)
        {
            RaiseHintAggregatesChanged();
        }
    }

    /// <summary>Outside a batch this appends at once; inside, the batch end appends and trims everything together.</summary>
    public void AppendEvent(EventEntry entry, int historyLimit = int.MaxValue)
    {
        _pendingEvents.Add(entry);
        _pendingEventHistoryLimit = Math.Min(_pendingEventHistoryLimit, Math.Max(1, historyLimit));

        if (_updateBatchDepth == 0)
        {
            FlushPendingEvents();
        }
    }

    private void FlushPendingEvents()
    {
        if (_pendingEvents.Count == 0)
        {
            return;
        }

        var limit = _pendingEventHistoryLimit;
        IReadOnlyList<EventEntry> toAdd = _pendingEvents.ToList();
        _pendingEvents.Clear();
        _pendingEventHistoryLimit = int.MaxValue;

        // Trim before appending, so a burst larger than the limit never shows more than the limit.
        var overflow = Events.Count + toAdd.Count - limit;
        if (overflow > 0)
        {
            var removeExisting = Math.Min(overflow, Events.Count);
            Events.RemoveRange(0, removeExisting);
            toAdd = toAdd.Skip(overflow - removeExisting).ToList();
        }

        Events.AddRange(toAdd);
    }

    /// <summary>"Mine" in the role filter = concerns any configured slot, not just the leader.</summary>
    public IEnumerable<HintEntry> VisibleHints => _visibleHints;

    private bool PassesHintFilters(HintEntry hint)
    {
        if (SelectedHintFilter == HintFilter.Unfound && hint.Found)
            return false;

        var passesRole = SelectedHintRoleFilter switch
        {
            HintRoleFilter.IFind    => IsMine(hint.FindingPlayerKind),
            HintRoleFilter.IReceive => IsMine(hint.ReceivingPlayerKind),
            _                       => true,
        };

        return passesRole
               && MatchesItemCategory(hint.ItemKind, SelectedHintItemCategoryFilter)
               && !(HideGoaledHints && hint.ReceivingSlot?.HasGoal == true)
               && (SelectedHintsSlotFilter is null || hint.SlotId == SelectedHintsSlotFilter.Id)
               && (string.IsNullOrEmpty(ItemSearchText) || hint.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesItemCategory(EventTextSegmentKind itemKind, ItemCategoryFilter filter) => filter switch
    {
        ItemCategoryFilter.Progress => itemKind == EventTextSegmentKind.ItemProgression,
        ItemCategoryFilter.Useful   => itemKind == EventTextSegmentKind.ItemUseful,
        ItemCategoryFilter.Normal   => itemKind == EventTextSegmentKind.ItemOther,
        ItemCategoryFilter.Trap     => itemKind == EventTextSegmentKind.ItemTrap,
        _                           => true,
    };

    private static bool IsMine(EventTextSegmentKind kind) =>
        kind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName;

    public int UnfoundHintCount => Hints.Count(h => !h.Found);

    public int UnfoundHintIFindCount => Hints.Count(h => !h.Found && IsMine(h.FindingPlayerKind));

    public int UnfoundHintIReceiveCount => Hints.Count(h => !h.Found && IsMine(h.ReceivingPlayerKind));

    public string HintsPanelButtonText => UnfoundHintCount > 0 ? $"Hints ({UnfoundHintCount})" : "Hints";

    public string HintsIFindButtonText => UnfoundHintIFindCount > 0 ? $"My location ({UnfoundHintIFindCount})" : "My location";

    public string HintsIReceiveButtonText => UnfoundHintIReceiveCount > 0 ? $"My item ({UnfoundHintIReceiveCount})" : "My item";

    /// <summary>Slot filtering follows <see cref="EventEntry.PassesSlotFilter"/>.</summary>
    public IEnumerable<EventEntry> VisibleEvents => _visibleEvents;

    private bool PassesEventFilters(EventEntry entry)
    {
        var passesRelevance = SelectedEventRelevanceFilter switch
        {
            EventRelevanceFilter.ConcernsMe => entry.ConcernsOwnSlot,
            // An item-send line's first segment is the finder, already classified as
            // an own slot when it is one. Own receipts start with plain "Received ".
            EventRelevanceFilter.FoundByMe => entry.Type == EventType.ItemReceived &&
                                              entry.Segments.Count > 0 &&
                                              IsMine(entry.Segments[0].Kind),
            _ => true,
        };

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
            _ => true, // no item class
        };

        return passesRelevance && passesCategory && passesItemClass
               && !(HideGoaledEvents && entry.Receiver?.HasGoal == true)
               && (SelectedEventsSlotFilter is null || entry.PassesSlotFilter(SelectedEventsSlotFilter.Id));
    }

    public IEnumerable<ReceivedItemEntry> VisibleReceivedItems => _visibleReceivedItems;

    private bool PassesReceivedItemFilters(ReceivedItemEntry item) =>
        MatchesItemCategory(item.ItemKind, SelectedItemCategoryFilter)
        && (SelectedItemsSlotFilter is null || item.SlotId == SelectedItemsSlotFilter.Id)
        && (string.IsNullOrEmpty(ItemSearchText) || item.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Also raises the property, though the instance stays the same: the filter summary texts piggyback on it, and
    /// the binding ignores an unchanged instance.
    /// </summary>
    private void RefreshVisibleEvents()
    {
        _visibleEvents.Refresh();
        OnPropertyChanged(nameof(VisibleEvents));
    }

    private void RefreshVisibleHints()
    {
        _visibleHints.Refresh();
        OnPropertyChanged(nameof(VisibleHints));
    }

    private void RefreshVisibleReceivedItems()
    {
        _visibleReceivedItems.Refresh();
        OnPropertyChanged(nameof(VisibleReceivedItems));
    }

    /// <summary>Summary for the collapsed Events filter bar; empty while nothing narrows the list.</summary>
    public string EventFilterSummaryText => FilterSummaryText.Join(
        FilterSummaryText.Relevance(SelectedEventRelevanceFilter),
        FilterSummaryText.Category(SelectedEventCategoryFilter),
        FilterSummaryText.ItemClasses(this),
        FilterSummaryText.HideGoaled(HideGoaledEvents),
        FilterSummaryText.Slot(SelectedEventsSlotFilter));

    /// <summary>Same for whichever of Hints/Items is showing.</summary>
    public string RightPanelFilterSummaryText => SelectedRightPanel == RightPanelView.Hints
        ? FilterSummaryText.Join(
            FilterSummaryText.HintFound(SelectedHintFilter),
            FilterSummaryText.HintRole(SelectedHintRoleFilter),
            FilterSummaryText.ItemCategory(SelectedHintItemCategoryFilter),
            FilterSummaryText.HideGoaled(HideGoaledHints),
            FilterSummaryText.Slot(SelectedHintsSlotFilter),
            FilterSummaryText.Search(ItemSearchText))
        : FilterSummaryText.Join(
            FilterSummaryText.ItemCategory(SelectedItemCategoryFilter),
            FilterSummaryText.Slot(SelectedItemsSlotFilter),
            FilterSummaryText.Search(ItemSearchText));

    public bool ShowAdminButton => Group.IsAdmin;

    public bool IsAdminPanelVisible => SelectedRightPanel == RightPanelView.Admin;

    /// <summary>Only Hints and Items have filters; the Admin view has its own header and Item links needs none.</summary>
    public bool IsRightPanelFilterBarVisible => SelectedRightPanel is RightPanelView.Hints or RightPanelView.ReceivedItems;

    partial void OnSelectedRightPanelChanged(RightPanelView value)
    {
        OnPropertyChanged(nameof(IsAdminPanelVisible));
        OnPropertyChanged(nameof(IsRightPanelFilterBarVisible));
    }

    /// <summary>Unticking "I'm the admin" must not leave the Admin view or the Admin event filter selected with no way back.</summary>
    private void OnIsAdminChanged()
    {
        OnPropertyChanged(nameof(ShowAdminButton));
        if (Group.IsAdmin)
        {
            return;
        }

        if (SelectedRightPanel == RightPanelView.Admin)
        {
            SelectedRightPanel = RightPanelView.Hints;
        }

        if (SelectedEventCategoryFilter == EventCategoryFilter.Admin)
        {
            SelectedEventCategoryFilter = EventCategoryFilter.All;
        }
    }

    /// <summary>Piggybacks the summary texts on the Visible* notifications every filter change already raises.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(VisibleEvents):
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(EventFilterSummaryText)));
                break;
            case nameof(VisibleHints):
            case nameof(VisibleReceivedItems):
            case nameof(SelectedRightPanel):
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(RightPanelFilterSummaryText)));
                break;
        }
    }

    partial void OnSelectedHintFilterChanged(HintFilter value) => RefreshVisibleHints();

    partial void OnSelectedHintRoleFilterChanged(HintRoleFilter value) => RefreshVisibleHints();

    partial void OnSelectedHintItemCategoryFilterChanged(ItemCategoryFilter value) => RefreshVisibleHints();

    partial void OnSelectedItemCategoryFilterChanged(ItemCategoryFilter value) => RefreshVisibleReceivedItems();

    partial void OnSelectedEventsSlotFilterChanged(SlotProfile? value) => RefreshVisibleEvents();

    partial void OnSelectedHintsSlotFilterChanged(SlotProfile? value) => RefreshVisibleHints();

    partial void OnSelectedItemsSlotFilterChanged(SlotProfile? value) => RefreshVisibleReceivedItems();

    partial void OnItemSearchTextChanged(string value)
    {
        RefreshVisibleHints();
        RefreshVisibleReceivedItems();
    }

    partial void OnSelectedEventRelevanceFilterChanged(EventRelevanceFilter value) => RefreshVisibleEvents();

    partial void OnSelectedEventCategoryFilterChanged(EventCategoryFilter value) => RefreshVisibleEvents();

    partial void OnHideGoaledEventsChanged(bool value) => RefreshVisibleEvents();

    partial void OnHideGoaledHintsChanged(bool value) => RefreshVisibleHints();

    partial void OnShowProgressionItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowUsefulItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowFillerItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    partial void OnShowTrapItemEventsChanged(bool value) => OnEventItemClassFilterChanged();

    private void OnEventItemClassFilterChanged()
    {
        RefreshVisibleEvents();
        OnPropertyChanged(nameof(EventItemClassFilterButtonText));
        OnPropertyChanged(nameof(IsEventItemClassFilterActive));
    }

    /// <inheritdoc/>
    public string EventItemClassFilterButtonText => EventItemClassFilterText.ButtonText(this);

    /// <inheritdoc/>
    public bool IsEventItemClassFilterActive => EventItemClassFilterText.IsActive(this);

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

        // VisibleHints already followed the change itself; only the counters are stale.
        MarkHintAggregatesChanged(visibleHintsStale: false);
    }

    private void OnHintEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HintEntry.Found))
        {
            MarkHintAggregatesChanged(visibleHintsStale: true);
        }
    }

    /// <summary>The counters scan every hint, so a batch recounts once at its end, not per hint.</summary>
    private void MarkHintAggregatesChanged(bool visibleHintsStale)
    {
        _visibleHintsStale |= visibleHintsStale;
        if (_updateBatchDepth > 0)
        {
            _hintAggregatesDirty = true;
            return;
        }

        RaiseHintAggregatesChanged();
    }

    private void RaiseHintAggregatesChanged()
    {
        _hintAggregatesDirty = false;
        if (_visibleHintsStale)
        {
            _visibleHintsStale = false;
            _visibleHints.Refresh();
        }

        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(UnfoundHintCount));
        OnPropertyChanged(nameof(UnfoundHintIFindCount));
        OnPropertyChanged(nameof(UnfoundHintIReceiveCount));
        OnPropertyChanged(nameof(HintsPanelButtonText));
        OnPropertyChanged(nameof(HintsIFindButtonText));
        OnPropertyChanged(nameof(HintsIReceiveButtonText));
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
    private void ShowAllHintItemCategories() => SelectedHintItemCategoryFilter = ItemCategoryFilter.All;

    [RelayCommand]
    private void ShowProgressHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Progress;

    [RelayCommand]
    private void ShowUsefulHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Useful;

    [RelayCommand]
    private void ShowNormalHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Normal;

    [RelayCommand]
    private void ShowTrapHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Trap;

    [RelayCommand]
    private void ShowAllEventsRelevance() => SelectedEventRelevanceFilter = EventRelevanceFilter.All;

    [RelayCommand]
    private void ShowOwnEventsOnly() => SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;

    [RelayCommand]
    private void ShowFoundByMeEvents() => SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

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
}
