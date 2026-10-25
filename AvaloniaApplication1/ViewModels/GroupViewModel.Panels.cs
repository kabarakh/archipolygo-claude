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

    [ObservableProperty]
    private RightPanelView _selectedRightPanel = RightPanelView.Hints;

    [ObservableProperty]
    private ItemCategoryFilter _selectedItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    public ObservableCollection<EventEntry> Events { get; } = new();

    public ObservableCollection<HintEntry> Hints { get; } = new();

    public ObservableCollection<ReceivedItemEntry> ReceivedItems { get; } = new();

    /// <summary>"Mine" in the role filter = concerns any configured slot, not just the leader.</summary>
    public IEnumerable<HintEntry> VisibleHints
    {
        get
        {
            IEnumerable<HintEntry> hints = Hints;

            if (SelectedHintFilter == HintFilter.Unfound)
                hints = hints.Where(h => !h.Found);

            hints = SelectedHintRoleFilter switch
            {
                HintRoleFilter.IFind    => hints.Where(h => IsMine(h.FindingPlayerKind)),
                HintRoleFilter.IReceive => hints.Where(h => IsMine(h.ReceivingPlayerKind)),
                _                       => hints,
            };

            hints = hints.Where(h => MatchesItemCategory(h.ItemKind, SelectedHintItemCategoryFilter));

            if (SelectedHintsSlotFilter is not null)
                hints = hints.Where(h => h.SlotId == SelectedHintsSlotFilter.Id);

            if (!string.IsNullOrEmpty(ItemSearchText))
                hints = hints.Where(h => h.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));

            return hints;
        }
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
    public IEnumerable<EventEntry> VisibleEvents
    {
        get
        {
            IEnumerable<EventEntry> events = Events;

            if (SelectedEventRelevanceFilter == EventRelevanceFilter.ConcernsMe)
            {
                events = events.Where(e => e.ConcernsOwnSlot);
            }
            else if (SelectedEventRelevanceFilter == EventRelevanceFilter.FoundByMe)
            {
                // An item-send line's first segment is the finder, already classified as
                // an own slot when it is one. Own receipts start with plain "Received ".
                events = events.Where(e =>
                    e.Type == EventType.ItemReceived &&
                    e.Segments.Count > 0 &&
                    (e.Segments[0].Kind == EventTextSegmentKind.OwnSlotName ||
                     e.Segments[0].Kind == EventTextSegmentKind.ConnectedSlotName));
            }

            events = SelectedEventCategoryFilter switch
            {
                EventCategoryFilter.Hints => events.Where(e => e.Type == EventType.HintReceived),
                EventCategoryFilter.Items => events.Where(e => e.Type == EventType.ItemReceived),
                EventCategoryFilter.Chat => events.Where(e => e.Type == EventType.Chat),
                EventCategoryFilter.Admin => events.Where(e => e.Type == EventType.Admin),
                _ => events,
            };

            events = events.Where(e => e.ItemKind switch
            {
                EventTextSegmentKind.ItemProgression => ShowProgressionItemEvents,
                EventTextSegmentKind.ItemUseful => ShowUsefulItemEvents,
                EventTextSegmentKind.ItemOther => ShowFillerItemEvents,
                EventTextSegmentKind.ItemTrap => ShowTrapItemEvents,
                _ => true, // no item class
            });

            if (SelectedEventsSlotFilter is not null)
            {
                var filterId = SelectedEventsSlotFilter.Id;
                events = events.Where(e => e.PassesSlotFilter(filterId));
            }

            return events;
        }
    }

    public IEnumerable<ReceivedItemEntry> VisibleReceivedItems
    {
        get
        {
            IEnumerable<ReceivedItemEntry> items = ReceivedItems;

            items = items.Where(i => MatchesItemCategory(i.ItemKind, SelectedItemCategoryFilter));

            if (SelectedItemsSlotFilter is not null)
                items = items.Where(i => i.SlotId == SelectedItemsSlotFilter.Id);

            if (!string.IsNullOrEmpty(ItemSearchText))
            {
                items = items.Where(i =>
                    i.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));
            }

            return items;
        }
    }

    /// <summary>Summary for the collapsed Events filter bar; empty while nothing narrows the list.</summary>
    public string EventFilterSummaryText => FilterSummaryText.Join(
        FilterSummaryText.Relevance(SelectedEventRelevanceFilter),
        FilterSummaryText.Category(SelectedEventCategoryFilter),
        FilterSummaryText.ItemClasses(this),
        FilterSummaryText.Slot(SelectedEventsSlotFilter));

    /// <summary>Same for whichever of Hints/Items is showing.</summary>
    public string RightPanelFilterSummaryText => SelectedRightPanel == RightPanelView.Hints
        ? FilterSummaryText.Join(
            FilterSummaryText.HintFound(SelectedHintFilter),
            FilterSummaryText.HintRole(SelectedHintRoleFilter),
            FilterSummaryText.ItemCategory(SelectedHintItemCategoryFilter),
            FilterSummaryText.Slot(SelectedHintsSlotFilter),
            FilterSummaryText.Search(ItemSearchText))
        : FilterSummaryText.Join(
            FilterSummaryText.ItemCategory(SelectedItemCategoryFilter),
            FilterSummaryText.Slot(SelectedItemsSlotFilter),
            FilterSummaryText.Search(ItemSearchText));

    public bool ShowAdminButton => Group.IsAdmin;

    /// <summary>Hides the Hints/Items filter bar; the Admin view has its own header.</summary>
    public bool IsAdminPanelVisible => SelectedRightPanel == RightPanelView.Admin;

    partial void OnSelectedRightPanelChanged(RightPanelView value) => OnPropertyChanged(nameof(IsAdminPanelVisible));

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

    partial void OnSelectedHintFilterChanged(HintFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedHintRoleFilterChanged(HintRoleFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedHintItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleReceivedItems));

    partial void OnSelectedEventsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnSelectedHintsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedItemsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleReceivedItems));

    partial void OnItemSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(VisibleReceivedItems));
    }

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

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(VisibleEvents));

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

    private void RaiseHintAggregatesChanged()
    {
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
