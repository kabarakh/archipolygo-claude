using System;

namespace Archipolygo.Services;

/// <summary>
/// Builds the exact "!admin ..." chat lines the Admin view sends (see
/// Admin-Funktionen.md in the feature-plan archive). The quoting rule differs
/// per command, checked against MultiServer.py's ServerCommandProcessor:
/// - "/send", "/send_multiple", "/send_location", "/option" are parsed with
///   <c>shlex.split</c>, so every name is quoted - that keeps names with
///   spaces together and is harmless otherwise.
/// - "/release", "/collect" (and the other <c>@mark_raw</c> commands) get the
///   raw rest of the line and compare it to the player name EXACTLY - quotes
///   would become part of the name and never match, so the name is sent bare.
/// </summary>
public static class AdminCommands
{
    public const string Prefix = "!admin ";

    /// <summary>MultiServer.py's /send_multiple rejects anything above this.</summary>
    public const int MaxSendAmount = 100;

    public static string Login(string password) => $"{Prefix}login {password}";

    public static string Logout() => $"{Prefix}logout";

    public static string SendItem(string playerName, string itemName, int amount)
    {
        amount = Math.Clamp(amount, 1, MaxSendAmount);
        return amount == 1
            ? $"{Prefix}/send {Quote(playerName)} {Quote(itemName)}"
            : $"{Prefix}/send_multiple {amount} {Quote(playerName)} {Quote(itemName)}";
    }

    public static string SendLocation(string playerName, string locationName) =>
        $"{Prefix}/send_location {Quote(playerName)} {Quote(locationName)}";

    public static string Release(string playerName) => $"{Prefix}/release {playerName}";

    public static string Collect(string playerName) => $"{Prefix}/collect {playerName}";

    public static string SetOption(string option, string value) => $"{Prefix}/option {option} {Quote(value)}";

    /// <summary>
    /// A double-quoted shlex token. Python's shlex (POSIX mode) treats a
    /// backslash inside double quotes as an escape only before <c>"</c> and
    /// <c>\</c> - unlike bash, not before <c>$</c> or a backtick - so exactly
    /// those two get escaped.
    /// </summary>
    internal static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>
    /// Lines that carry a password - never kept in the chat box's Up-arrow
    /// history (the server itself already stars them out for everyone else).
    /// </summary>
    public static bool ContainsPassword(string chatLine)
    {
        var trimmed = chatLine.TrimStart();
        return trimmed.StartsWith("!admin login", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("!admin /option server_password", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("!admin /option password", StringComparison.OrdinalIgnoreCase);
    }
}
