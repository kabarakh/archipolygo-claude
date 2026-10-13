namespace Archipolygo.Models;

/// <summary>
/// Server settings from the library's RoomState for the Admin settings dialog. Mode values are the exact strings
/// "/option" accepts; countdown_mode and item_cheat aren't in RoomState, so their current value is unknown.
/// </summary>
public sealed class RoomSettingsSnapshot
{
    public required string ReleaseMode { get; init; }
    public required string CollectMode { get; init; }
    public required string RemainingMode { get; init; }
    public required int HintCostPercentage { get; init; }
    public required int LocationCheckPoints { get; init; }
    public required bool HasPassword { get; init; }
}
