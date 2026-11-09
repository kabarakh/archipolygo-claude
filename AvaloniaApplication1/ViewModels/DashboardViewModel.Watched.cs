using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>Every server's watched slot: the event-log highlight and the "Watched only" list.</summary>
public partial class DashboardViewModel
{
    private readonly Dictionary<GroupViewModel, NotifyCollectionChangedEventHandler> _watchedItemsHandlers = new();

    [ObservableProperty]
    private bool _highlightWatchedEvents = true;

    /// <summary>Swaps the event log for <see cref="WatchedItemsAcrossGroups"/>.</summary>
    [ObservableProperty]
    private bool _showWatchedOnly;

    /// <summary>Server by server, newest first within each.</summary>
    public RangeObservableCollection<DashboardWatchedRow> WatchedItemsAcrossGroups { get; } = new();

    public bool HasWatchedSlot => _groups.Any(group => group.HasWatchedSlot);

    public bool HasNoWatchedItems => WatchedItemsAcrossGroups.Count == 0;

    public bool IsEventLogVisible => !ShowWatchedOnly;

    [RelayCommand]
    private void ToggleWatchedOnly() => ShowWatchedOnly = !ShowWatchedOnly;

    partial void OnShowWatchedOnlyChanged(bool value) => OnPropertyChanged(nameof(IsEventLogVisible));

    private void SubscribeWatchedItems(GroupViewModel group)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => RebuildWatchedItemsAcrossGroups();
        _watchedItemsHandlers[group] = handler;
        group.WatchedItems.CollectionChanged += handler;
    }

    private void UnsubscribeWatchedItems(GroupViewModel group)
    {
        if (_watchedItemsHandlers.Remove(group, out var handler))
        {
            group.WatchedItems.CollectionChanged -= handler;
        }
    }

    /// <summary>Cheap: each server keeps only its last few items.</summary>
    private void RebuildWatchedItemsAcrossGroups()
    {
        WatchedItemsAcrossGroups.ResetTo(_groups
            .Where(group => group.HasWatchedSlot)
            .SelectMany(group => group.WatchedItems.Select(item => new DashboardWatchedRow(group, item)))
            .ToList());
        OnPropertyChanged(nameof(HasNoWatchedItems));
        OnPropertyChanged(nameof(HasWatchedSlot));

        if (!HasWatchedSlot)
        {
            ShowWatchedOnly = false;
        }
    }
}
