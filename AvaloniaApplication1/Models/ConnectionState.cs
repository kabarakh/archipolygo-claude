namespace Archipolygo.Models;

/// <summary>
/// Connection status of a single connection tab.
/// </summary>
public enum ConnectionState
{
    Disconnected,

    /// <summary>
    /// Waiting its turn in <see cref="Archipolygo.Services.ConnectionManager"/>'s
    /// connect queue; the actual handshake hasn't started yet (see
    /// <see cref="Connecting"/>).
    /// </summary>
    Queued,

    /// <summary>The handshake (ConnectAsync/LoginAsync) is actively in progress.</summary>
    Connecting,

    Connected,
    Reconnecting,
    Error
}
