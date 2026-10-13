namespace Archipolygo.Models;

public enum ConnectionState
{
    Disconnected,

    /// <summary>Waiting in ConnectionManager's connect queue; the handshake hasn't started yet.</summary>
    Queued,

    /// <summary>The handshake (ConnectAsync/LoginAsync) is actively in progress.</summary>
    Connecting,

    Connected,
    Reconnecting,
    Error
}
