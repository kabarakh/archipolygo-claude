using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One row in the "Edit server" dialog's slot list; <see cref="IsDefaultLeader"/> is precomputed per row
/// to avoid a cross-binding against the group's <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>.
/// </summary>
public partial class ConfiguredSlotRow : ObservableObject
{
    public required SlotProfile Slot { get; init; }

    [ObservableProperty]
    private bool _isDefaultLeader;
}
