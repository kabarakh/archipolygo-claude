namespace Archipolygo.Models;

/// <summary>
/// One of a slot's own unchecked locations for the "Hint..." picker's Location mode; the picker itself
/// additionally drops already-hinted ones.
/// </summary>
public sealed record HintableLocation
{
    public required long LocationId { get; init; }
    public required string Name { get; init; }
}
