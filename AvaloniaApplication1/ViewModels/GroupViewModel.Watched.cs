using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>The watched slot: its progression-item strip, its slot-filter entry and its highlight in the event log.</summary>
public partial class GroupViewModel
{
    private const int NoWatchedSlotNumber = -1;

    // Session callbacks read this off the UI thread, so it's a plain int instead of an observable property.
    private int _watchedSlotNumber = NoWatchedSlotNumber;

    private RoomSlotStatus? _watchedSlotStatus;

    /// <summary>Slot-filter entry for a non-configured watched slot; never part of <c>Group.Slots</c> or <see cref="Slots"/>.</summary>
    private SlotProfile? _watchedFilterEntry;

    /// <summary>Newest first, at most <see cref="ServerConnectionGroup.WatchedItemCount"/>.</summary>
    public ObservableCollection<WatchedItemRow> WatchedItems { get; } = new();

    /// <summary>Raised on the UI thread when a row marked new was added.</summary>
    public event Action? NewWatchedItemsAdded;

    [ObservableProperty]
    private bool _highlightWatchedEvents = true;

    public bool HasWatchedSlot => !string.IsNullOrWhiteSpace(Group.WatchedSlotName);

    public string WatchedSlotName => Group.WatchedSlotName ?? string.Empty;

    public bool IsWatchedSlotOwn => HasWatchedSlot && FindConfiguredSlot(WatchedSlotName) is not null;

    public string WatchedSlotHeaderText => IsWatchedSlotOwn ? $"{WatchedSlotName} (own slot)" : WatchedSlotName;

    public bool WatchedSlotHasGoal => _watchedSlotStatus?.HasGoal == true;

    public WatchedItemRow? LatestWatchedItem => WatchedItems.FirstOrDefault();

    public bool HasWatchedItems => WatchedItems.Count > 0;

    public int NewWatchedItemCount => WatchedItems.Count(row => row.IsNew);

    public bool HasNewWatchedItems => NewWatchedItemCount > 0;

    public string NewWatchedItemCountText => $"{NewWatchedItemCount} new";

    public string WatchedItemsCaption => $"last {Group.WatchedItemCount} progression items";

    /// <summary>Collapsed, the strip's header shows the latest item instead of the caption.</summary>
    public bool ShowWatchedLatestInHeader => !FilterLayout.WatchedItemsExpanded && HasWatchedItems;

    public bool ShowWatchedCaptionInHeader => !ShowWatchedLatestInHeader;

    public bool ShowNoWatchedItemsText => FilterLayout.WatchedItemsExpanded && !HasWatchedItems;

    public string WatchedOverviewText => LatestWatchedItem is { } latest
        ? $"{WatchedSlotName}: {latest.ItemName}"
        : $"{WatchedSlotName}: no progression items yet";

    /// <summary>The watched slot's number in the room once a leader resolved it; safe to read off the UI thread.</summary>
    public int? WatchedSlotNumber
    {
        get
        {
            var number = Volatile.Read(ref _watchedSlotNumber);
            return number == NoWatchedSlotNumber ? null : number;
        }
    }

    public bool IsWatchedSlotName(string slotName) =>
        Group.WatchedSlotName is { Length: > 0 } watched && string.Equals(watched, slotName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Thread-safe; follow up with <see cref="ApplyWatchedSlotStatuses"/> on the UI thread.</summary>
    public void SetWatchedSlotNumber(int? slotNumber) =>
        Volatile.Write(ref _watchedSlotNumber, slotNumber ?? NoWatchedSlotNumber);

    /// <summary>
    /// Flags the watched slot and every item-link group it belongs to, since items sent to such a group
    /// name the group as receiver. Reruns when the item links are read.
    /// </summary>
    public void ApplyWatchedSlotStatuses()
    {
        foreach (var status in _roomSlotStatuses.Values.Where(status => status.IsWatched))
        {
            status.IsWatched = false;
        }

        if (_watchedSlotStatus is not null)
        {
            _watchedSlotStatus.PropertyChanged -= OnWatchedSlotStatusChanged;
            _watchedSlotStatus = null;
        }

        if (WatchedSlotNumber is { } number)
        {
            _watchedSlotStatus = GetRoomSlotStatus(number);
            _watchedSlotStatus.IsWatched = true;
            _watchedSlotStatus.PropertyChanged += OnWatchedSlotStatusChanged;

            foreach (var link in ItemLinkGroups.Where(link => link.Members.Any(member => member.Slot.SlotNumber == number)))
            {
                link.Slot.IsWatched = true;
            }
        }

        OnPropertyChanged(nameof(WatchedSlotHasGoal));
    }

    private void OnWatchedSlotStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RoomSlotStatus.HasGoal))
        {
            OnPropertyChanged(nameof(WatchedSlotHasGoal));
        }
    }

    /// <summary>A live item; UI thread (via <see cref="Services.UiUpdateQueue"/>).</summary>
    public void AddWatchedItem(WatchedItemRow row)
    {
        WatchedItems.Insert(0, row);
        TrimWatchedItems();
        RaiseWatchedItemsChanged();

        if (row.IsNew)
        {
            NewWatchedItemsAdded?.Invoke();
        }
    }

    /// <summary>The whole history after a catch-up; UI thread (via <see cref="Services.UiUpdateQueue"/>).</summary>
    public void ReplaceWatchedItems(IEnumerable<WatchedItemRow> rowsNewestFirst)
    {
        WatchedItems.Clear();
        foreach (var row in rowsNewestFirst.Take(Math.Max(1, Group.WatchedItemCount)))
        {
            WatchedItems.Add(row);
        }

        RaiseWatchedItemsChanged();

        if (WatchedItems.Any(row => row.IsNew))
        {
            NewWatchedItemsAdded?.Invoke();
        }
    }

    public void MarkWatchedItemsSeen()
    {
        foreach (var row in WatchedItems.Where(row => row.IsNew))
        {
            row.IsNew = false;
        }

        RaiseWatchedItemsChanged();
    }

    private void TrimWatchedItems()
    {
        var limit = Math.Max(1, Group.WatchedItemCount);
        while (WatchedItems.Count > limit)
        {
            WatchedItems.RemoveAt(WatchedItems.Count - 1);
        }
    }

    private void RaiseWatchedItemsChanged()
    {
        OnPropertyChanged(nameof(LatestWatchedItem));
        OnPropertyChanged(nameof(HasWatchedItems));
        OnPropertyChanged(nameof(NewWatchedItemCount));
        OnPropertyChanged(nameof(HasNewWatchedItems));
        OnPropertyChanged(nameof(NewWatchedItemCountText));
        OnPropertyChanged(nameof(WatchedOverviewText));
        RaiseWatchedHeaderLayoutChanged();
    }

    private void RaiseWatchedHeaderLayoutChanged()
    {
        OnPropertyChanged(nameof(ShowWatchedLatestInHeader));
        OnPropertyChanged(nameof(ShowWatchedCaptionInHeader));
        OnPropertyChanged(nameof(ShowNoWatchedItemsText));
    }

    /// <summary>A different slot starts from scratch: the leader resolves its number and the catch-up refills the strip.</summary>
    private void OnWatchedSlotNameChanged()
    {
        SetWatchedSlotNumber(null);
        ApplyWatchedSlotStatuses();
        WatchedItems.Clear();
        UpdateWatchedFilterEntry();

        OnPropertyChanged(nameof(HasWatchedSlot));
        OnPropertyChanged(nameof(WatchedSlotName));
        RaiseWatchedSlotKindChanged();
        RaiseWatchedItemsChanged();
    }

    private void RaiseWatchedSlotKindChanged()
    {
        OnPropertyChanged(nameof(IsWatchedSlotOwn));
        OnPropertyChanged(nameof(WatchedSlotHeaderText));
    }

    private void OnWatchedItemCountChanged()
    {
        TrimWatchedItems();
        OnPropertyChanged(nameof(WatchedItemsCaption));
        RaiseWatchedItemsChanged();
    }

    private void OnFilterLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterBarLayoutState.WatchedItemsExpanded))
        {
            RaiseWatchedHeaderLayoutChanged();
        }
    }

    /// <summary>
    /// A configured watched slot already has its own filter entry; any other watched slot gets one at the end,
    /// keyed by <see cref="ServerConnectionGroup.WatchedSlotId"/>.
    /// </summary>
    private void UpdateWatchedFilterEntry()
    {
        var wanted = HasWatchedSlot && !IsWatchedSlotOwn;
        if (_watchedFilterEntry is not null &&
            (!wanted || _watchedFilterEntry.Id != Group.WatchedSlotId || !IsWatchedSlotName(_watchedFilterEntry.SlotName)))
        {
            ClearSlotFiltersPointingAt(_watchedFilterEntry);
            SlotFilterOptions.Remove(_watchedFilterEntry);
            _watchedFilterEntry = null;
        }

        if (wanted && _watchedFilterEntry is null)
        {
            _watchedFilterEntry = new SlotProfile { Id = Group.WatchedSlotId, SlotName = WatchedSlotName, Alias = "watched" };
            SlotFilterOptions.Add(_watchedFilterEntry);
        }
    }

    private SlotProfile? FindConfiguredSlot(string slotName) =>
        Group.Slots.FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void ToggleWatchedItemsExpanded() => FilterLayout.WatchedItemsExpanded = !FilterLayout.WatchedItemsExpanded;
}
