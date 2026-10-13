namespace Archipolygo.Models;

/// <summary>A room player queued in the "Add slot" dialog, with the password override typed at staging time.</summary>
public class StagedSlot
{
    public required string SlotName { get; init; }
    public required string DisplayText { get; init; }

    /// <summary>Null to use the group's shared password.</summary>
    public string? Password { get; init; }
}
