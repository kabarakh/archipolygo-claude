namespace Archipolygo.Models;

/// <summary>
/// Server text that can answer a command (checked against MultiServer.py): "!" commands, including every
/// "!admin login" outcome, answer as <see cref="CommandResult"/>; failing "!admin /..." commands as
/// <see cref="AdminCommandResult"/>; most successful admin actions ("/send", "/release") only as a room-wide <see cref="Broadcast"/>.
/// </summary>
public enum ServerReplyKind
{
    CommandResult,
    AdminCommandResult,
    Broadcast,
}
