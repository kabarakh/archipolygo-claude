using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// "Pick a server, then optionally one of its slots" cascade for the Dashboard's Hints and Events views.
/// Display-only: selecting a slot here never touches a live connection or the leader.
/// </summary>
public sealed partial class DashboardServerSlotFilter : ObservableObject
{
    private readonly ObservableCollection<GroupViewModel> _groups;

    /// <summary>Leading null entry = "All servers" (labelled by <see cref="Converters.DashboardServerFilterDisplayConverter"/>).</summary>
    public ObservableCollection<GroupViewModel?> ServerOptions { get; } = new();

    [ObservableProperty]
    private GroupViewModel? _selectedServer;

    /// <summary>Leading null entry = "All slots"; only that while no server is picked, since a cross-server slot list would be ambiguous.</summary>
    public ObservableCollection<SlotProfile?> SlotOptions { get; } = new() { null };

    [ObservableProperty]
    private SlotProfile? _selectedSlot;

    public DashboardServerSlotFilter(ObservableCollection<GroupViewModel> groups)
    {
        _groups = groups;
        RebuildServerOptions();
    }

    public void RebuildServerOptions()
    {
        var previousSelection = SelectedServer;

        ServerOptions.Clear();
        ServerOptions.Add(null);
        foreach (var group in _groups)
        {
            ServerOptions.Add(group);
        }

        if (previousSelection is not null && !_groups.Contains(previousSelection))
        {
            SelectedServer = null;
        }
    }

    public void HandleGroupRemoved(GroupViewModel group)
    {
        if (SelectedServer == group)
        {
            SelectedServer = null;
        }
    }

    /// <summary>
    /// Copies the group's <see cref="GroupViewModel.SlotFilterOptions"/> rather than binding to it, so a slot
    /// change on that server doesn't mutate this dropdown's ItemsSource in place.
    /// </summary>
    partial void OnSelectedServerChanged(GroupViewModel? oldValue, GroupViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.SlotFilterOptions.CollectionChanged -= OnSelectedServerSlotFilterOptionsChanged;
        }

        if (newValue is not null)
        {
            newValue.SlotFilterOptions.CollectionChanged += OnSelectedServerSlotFilterOptionsChanged;
        }

        RebuildSlotOptions();
        SelectedSlot = null;
    }

    private void OnSelectedServerSlotFilterOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var previousSlot = SelectedSlot;
        RebuildSlotOptions();
        SelectedSlot = previousSlot is not null && SlotOptions.Contains(previousSlot) ? previousSlot : null;
    }

    private void RebuildSlotOptions()
    {
        SlotOptions.Clear();
        SlotOptions.Add(null);

        if (SelectedServer is null)
        {
            return;
        }

        foreach (var slot in SelectedServer.SlotFilterOptions)
        {
            if (slot is not null)
            {
                SlotOptions.Add(slot);
            }
        }
    }
}
