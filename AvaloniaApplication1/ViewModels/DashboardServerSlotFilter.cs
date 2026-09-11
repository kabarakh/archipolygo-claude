using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// Reusable "pick a server, then (optionally) narrow to one of its slots"
/// display filter cascade - shared by <see cref="DashboardViewModel"/>'s
/// Hints and Events views (<see cref="DashboardViewModel.VisibleHints"/>/
/// <see cref="DashboardViewModel.VisibleEvents"/>), which used to each
/// hand-roll an identical copy of this (rebuild/reset-on-server-change/
/// track-the-selected-server's-own-slot-list-changing) with nothing but
/// property names differing - see Feature-Plaene/Archiv/Dashboard-Tab.md's
/// Status note.
///
/// A pure display filter, nothing more: selecting a slot here never
/// touches any live connection/leader - that's <see cref="GroupViewModel.SelectedChatSlot"/>'s
/// job (see <see cref="DashboardViewModel.SelectedSendServerGroup"/> for the
/// one place in this app that deliberately does switch the leader).
/// </summary>
public sealed partial class DashboardServerSlotFilter : ObservableObject
{
    private readonly ObservableCollection<GroupViewModel> _groups;

    /// <summary>Leading null entry = "All servers"; a <see cref="Converters.DashboardServerFilterDisplayConverter"/> turns it into that label in the UI.</summary>
    public ObservableCollection<GroupViewModel?> ServerOptions { get; } = new();

    [ObservableProperty]
    private GroupViewModel? _selectedServer;

    /// <summary>Leading null entry = "All slots". Stays at just that single entry while <see cref="SelectedServer"/> is null - a flat cross-server slot list wouldn't be unambiguous.</summary>
    public ObservableCollection<SlotProfile?> SlotOptions { get; } = new() { null };

    [ObservableProperty]
    private SlotProfile? _selectedSlot;

    public DashboardServerSlotFilter(ObservableCollection<GroupViewModel> groups)
    {
        _groups = groups;
        RebuildServerOptions();
    }

    /// <summary>Call whenever the owning <see cref="DashboardViewModel"/>'s <c>Groups</c> collection changes (add/remove a server).</summary>
    public void RebuildServerOptions()
    {
        var previousSelection = SelectedServer;

        ServerOptions.Clear();
        ServerOptions.Add(null);
        foreach (var group in _groups)
        {
            ServerOptions.Add(group);
        }

        // A group that got removed can't stay selected - OnSelectedServerChanged
        // (triggered by this assignment) takes care of resetting the slot filter too.
        if (previousSelection is not null && !_groups.Contains(previousSelection))
        {
            SelectedServer = null;
        }
    }

    /// <summary>Call when a specific group is being removed - resets the selection if it was the one picked here (mirrors <see cref="RebuildServerOptions"/>'s own removal handling, for the case where the caller already knows exactly which group is gone rather than rebuilding the whole list first).</summary>
    public void HandleGroupRemoved(GroupViewModel group)
    {
        if (SelectedServer == group)
        {
            SelectedServer = null;
        }
    }

    /// <summary>
    /// Server changed: rebuild <see cref="SlotOptions"/> from the newly
    /// selected group's own (already-maintained) <see cref="GroupViewModel.SlotFilterOptions"/> -
    /// copied into a new collection rather than bound directly, so an
    /// insert/remove there doesn't also mutate this dropdown's ItemsSource
    /// in place (a slot added/removed on one server must not visibly affect
    /// a completely unrelated filter dropdown elsewhere). Always resets
    /// <see cref="SelectedSlot"/> to null - it can't still refer to a slot
    /// on the new server anyway.
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

    /// <summary>The selected server's own slot list changed (slot added/removed while this filter has that server picked) - refresh the copy, preserving the current selection if it's still valid.</summary>
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
