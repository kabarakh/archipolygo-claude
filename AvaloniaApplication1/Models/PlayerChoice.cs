namespace Archipolygo.Models;

/// <summary>
/// One selectable entry in the "Add slot" dialog's player dropdown: a player
/// currently in the Archipelago room that isn't already configured as a slot
/// on this server. <see cref="SlotName"/> is what's actually sent as the
/// login slot name; <see cref="DisplayText"/> is what the dropdown shows,
/// which also includes the alias when it differs from the slot name.
/// </summary>
public class PlayerChoice
{
    public required string SlotName { get; init; }
    public required string DisplayText { get; init; }
}
