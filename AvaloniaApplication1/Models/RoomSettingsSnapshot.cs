namespace Archipolygo.Models;

/// <summary>
/// The server settings the Admin view's settings dialog can show from the
/// live connection (the library's <c>RoomState</c>) - see
/// <see cref="Services.IConnectionManager.GetRoomSettings"/>. Mode values use
/// the exact strings "/option" accepts ("disabled", "enabled", "goal",
/// "auto", "auto_enabled"). countdown_mode and item_cheat aren't part of
/// RoomState at all, so the dialog can't show their current value.
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
