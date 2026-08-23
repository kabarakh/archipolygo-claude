namespace Archipolygo.Models;

/// <summary>
/// A room player queued up in the "Add slot" dialog to actually be added
/// once the dialog is confirmed - see <see cref="ViewModels.ConnectionEditorViewModel.StagedSlots"/>.
/// Unlike <see cref="PlayerChoice"/> (which just identifies a pickable room
/// player), this also carries whatever per-slot password override was typed
/// in at the moment this player was staged, so several slots - each
/// possibly needing its own different password on a custom-hosted room -
/// can be queued up and added together in one go instead of one dialog
/// round-trip per slot.
/// </summary>
public class StagedSlot
{
    public required string SlotName { get; init; }
    public required string DisplayText { get; init; }

    /// <summary>Per-slot password override typed in while staging this entry, or null to just use the group's shared password.</summary>
    public string? Password { get; init; }
}
