namespace Archipolygo.Models;

/// <summary>A webhosted room's connection details resolved from its room id, so a room link can replace "host:port".</summary>
public sealed class RoomConnectionInfo
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>Null if the room_status response had no tracker id.</summary>
    public string? TrackerId { get; init; }
}
