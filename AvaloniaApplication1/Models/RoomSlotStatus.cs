using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// Live state of one slot number in a room, shared by every entry, segment and hint that names it,
/// so a later goal or watch re-renders older lines too. Changes on the UI thread only.
/// </summary>
public sealed partial class RoomSlotStatus : ObservableObject
{
    public RoomSlotStatus(int slotNumber) => SlotNumber = slotNumber;

    public int SlotNumber { get; }

    [ObservableProperty]
    private bool _hasGoal;

    /// <summary>The group's watched slot, or an item-link group it belongs to; drives the event-log highlight.</summary>
    [ObservableProperty]
    private bool _isWatched;

    /// <summary>Set only for an item-link group: its members, one per line. Drives the tooltip on the group's name.</summary>
    [ObservableProperty]
    private string? _itemLinkMembersText;
}
