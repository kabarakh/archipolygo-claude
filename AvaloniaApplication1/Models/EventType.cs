namespace Archipolygo.Models;

/// <summary>
/// Kind of a logged <see cref="EventEntry"/>.
/// </summary>
public enum EventType
{
    Connected,
    Disconnected,
    ItemReceived,
    HintReceived,
    Chat,
    Error
}
