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
    Error,

    /// <summary>
    /// An incoming DeathLink (see Feature-Plaene/Archiv/DeathLink.md) - display
    /// only, this app never sends one.
    /// </summary>
    DeathLink
}
