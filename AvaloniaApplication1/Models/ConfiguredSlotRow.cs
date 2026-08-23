using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One row in the "Edit server" dialog's slot-management list - wraps a
/// <see cref="SlotProfile"/> already configured on the group together with
/// whether it's currently the server's default/preferred leader (see
/// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>), computed once
/// up front so the view can show/toggle it per row without a cross-binding
/// comparison between this row's own slot id and a separate view-model-level
/// property.
/// </summary>
public partial class ConfiguredSlotRow : ObservableObject
{
    public required SlotProfile Slot { get; init; }

    [ObservableProperty]
    private bool _isDefaultLeader;
}
