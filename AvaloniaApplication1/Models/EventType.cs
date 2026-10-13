namespace Archipolygo.Models;

public enum EventType
{
    Connected,
    Disconnected,
    ItemReceived,
    HintReceived,
    Chat,
    Error,

    /// <summary>An incoming DeathLink - display only, this app never sends one.</summary>
    DeathLink,

    /// <summary>
    /// Everything around "!admin": its echo, login/logout answers, command results,
    /// "Cheat console: ..." broadcasts and this app's own admin-login notes.
    /// </summary>
    Admin
}
