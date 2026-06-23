namespace Archipolygo.Models;

/// <summary>
/// Connection status of a single connection tab.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}
