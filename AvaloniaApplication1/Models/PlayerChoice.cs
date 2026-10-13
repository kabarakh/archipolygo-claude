namespace Archipolygo.Models;

/// <summary>
/// A room player not yet configured on this server. <see cref="SlotName"/> is the login name;
/// <see cref="DisplayText"/> also includes a differing alias.
/// </summary>
public class PlayerChoice
{
    public required string SlotName { get; init; }
    public required string DisplayText { get; init; }
}
