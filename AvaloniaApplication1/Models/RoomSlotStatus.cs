using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// Live state of one slot number in a room, shared by every entry, segment and hint that names it,
/// so a later goal re-renders older lines too. <see cref="HasGoal"/> changes on the UI thread only.
/// </summary>
public sealed partial class RoomSlotStatus : ObservableObject
{
    public RoomSlotStatus(int slotNumber) => SlotNumber = slotNumber;

    public int SlotNumber { get; }

    [ObservableProperty]
    private bool _hasGoal;

    /// <summary>Set only for an item-link group: its members, one per line. Drives the tooltip on the group's name.</summary>
    [ObservableProperty]
    private string? _itemLinkMembersText;
}
