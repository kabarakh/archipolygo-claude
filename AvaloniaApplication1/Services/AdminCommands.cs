using System;

namespace Archipolygo.Services;

/// <summary>
/// Builds "!admin ..." lines. MultiServer.py parses /send, /send_multiple, /send_location and /option with
/// <c>shlex.split</c> (names quoted), but /release and /collect (<c>@mark_raw</c>) compare the raw rest of the line
/// to the name exactly, so those must send it bare.
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

    /// <summary>POSIX shlex only treats a backslash in double quotes as an escape before <c>"</c> and <c>\</c>.</summary>
    internal static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>Lines that carry a password are never kept in the chat box's Up-arrow history.</summary>
    public static bool ContainsPassword(string chatLine)
    {
        var trimmed = chatLine.TrimStart();
        return trimmed.StartsWith("!admin login", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("!admin /option server_password", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("!admin /option password", StringComparison.OrdinalIgnoreCase);
    }
}
