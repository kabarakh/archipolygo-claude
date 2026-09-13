namespace Archipolygo.Models;

/// <summary>
/// A webhosted room's actual connection details, resolved from its room id -
/// see <see cref="Services.IMultiworldTrackerService.ResolveRoomConnectionInfoAsync"/>.
/// Lets <see cref="ViewModels.ConnectionEditorViewModel"/> accept a room
/// link/id in place of a typed "host:port" when creating or editing a
/// server.
/// </summary>
public sealed class RoomConnectionInfo
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>The room's tracker id, if it has one (Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md) - null if the room_status response had none.</summary>
    public string? TrackerId { get; init; }
}
