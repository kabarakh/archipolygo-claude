namespace Archipolygo.Models;

/// <summary>
/// The kinds of server text <see cref="Services.IConnectionManager.ServerReplyReceived"/>
/// reports - the ones that can be an answer to a command this app sent.
/// Checked against MultiServer.py: "!" commands answer as
/// <see cref="CommandResult"/> (including every "!admin login" outcome),
/// failing "!admin /..." commands as <see cref="AdminCommandResult"/>, and
/// most SUCCESSFUL admin actions (e.g. "/send", "/release") only as a
/// room-wide <see cref="Broadcast"/> without any direct reply.
/// </summary>
public enum ServerReplyKind
{
    CommandResult,
    AdminCommandResult,
    Broadcast,
}
