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
}
