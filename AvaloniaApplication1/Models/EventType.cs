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
    DeathLink,

    /// <summary>
    /// Everything around "!admin" (Admin-Funktionen.md in the feature-plan
    /// archive): the server's echo of an "!admin ..." line, login/logout
    /// answers, admin command results, "Cheat console: ..." broadcasts, and
    /// this app's own notes about the admin login. See
    /// <see cref="Services.AdminEventFormatter"/>.
    /// </summary>
    Admin
}
