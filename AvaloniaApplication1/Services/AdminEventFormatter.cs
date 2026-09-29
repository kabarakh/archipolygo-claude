using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>Where a leader log line came from, as far as <see cref="AdminEventFormatter.IsAdminLine"/> cares.</summary>
public enum AdminLineSource
{
    /// <summary>A player's chat line - its message text, without the "Name: " prefix.</summary>
    Chat,
    CommandResult,
    AdminCommandResult,

    /// <summary>An untyped PrintJSON (MultiServer.py's broadcast_text_all), e.g. "Cheat console: ...".</summary>
    Broadcast,
}

/// <summary>
/// Picks out the log lines that belong to <see cref="EventType.Admin"/> and
/// colors them. MultiServer.py sends every admin-related line as plain text
/// (no player/item parts), so unlike every other event line there's nothing
/// to color from - names are found in the text instead:
/// - every known room player's slot name and alias, colored like
///   everywhere else (own slot / related slot / anyone else);
/// - in "/send" lines ("!admin /send ..." echo, "Cheat console: sending
///   ..."), the quoted item name. Always the normal item color: the server
///   gives a cheated item no classification at all (its flags are 0), so
///   there's no progression/useful/trap to show.
/// </summary>
public static class AdminEventFormatter
{
    private static readonly string[] AdminCommandResultPhrases =
    {
        "Login successful",
        "Password incorrect",
        "Remote administration is disabled",
        "must first login",
        "Logout successful",
        "Usage: !admin",
    };

    private static readonly Regex QuotedText = new("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

    // The option answer, plus - as a safety net, the server already stars
    // these out itself - the "!admin login"/"/option (server_)password" echo.
    private static readonly Regex PasswordOptionEcho = new(
        @"(Set option (?:server_password|password) to |!admin login |!admin /option (?:server_password|password) ).+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The admin password is never persisted and never shown after it was
    /// typed: MultiServer.py stars it out in the "!admin login"/"!admin
    /// /option server_password" echo, but its own direct answer to "/option"
    /// ("Set option server_password to &lt;new&gt;", only sent to the admin)
    /// repeats the value in plain text. This masks that answer (and the
    /// room-password equivalent) before it reaches the event log or a dialog -
    /// and with that "Copy from here"/log export.
    /// </summary>
    public static string MaskPasswords(string text) =>
        PasswordOptionEcho.Replace(text, match => match.Groups[1].Value + new string('*', 8));

    public static bool IsAdminLine(AdminLineSource source, string text) => source switch
    {
        AdminLineSource.AdminCommandResult => true,
        AdminLineSource.Chat => text.TrimStart().StartsWith("!admin", StringComparison.OrdinalIgnoreCase),
        AdminLineSource.CommandResult => AdminCommandResultPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)),
        AdminLineSource.Broadcast => text.StartsWith("Cheat console:", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// <paramref name="players"/>: every name (slot name or alias) that may
    /// appear in the text, with the color it should get.
    /// </summary>
    public static IReadOnlyList<EventTextSegment> BuildSegments(string text, IReadOnlyList<(string Name, EventTextSegmentKind Kind)> players)
    {
        // start -> (length, kind); non-overlapping.
        var marks = new SortedDictionary<int, (int Length, EventTextSegmentKind Kind)>();
        var nameKinds = new Dictionary<string, EventTextSegmentKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, kind) in players.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            nameKinds.TryAdd(name, kind);
        }

        var isSendLine = text.Contains("Cheat console: sending", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("!admin /send ", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("!admin /send_multiple ", StringComparison.OrdinalIgnoreCase);

        // Quoted names first: a quoted player name as a whole, and (in a
        // "/send" line) the quoted item.
        foreach (Match match in QuotedText.Matches(text))
        {
            var inner = match.Groups[1];
            if (inner.Length == 0)
            {
                continue;
            }

            if (nameKinds.TryGetValue(inner.Value, out var playerKind))
            {
                marks[inner.Index] = (inner.Length, playerKind);
            }
            else if (isSendLine)
            {
                marks[inner.Index] = (inner.Length, EventTextSegmentKind.ItemOther);
            }
        }

        // Then every other occurrence of a known name, longest first so
        // "Done Dan" wins over "Done".
        foreach (var (name, kind) in nameKinds.OrderByDescending(p => p.Key.Length))
        {
            var searchFrom = 0;
            while (searchFrom < text.Length)
            {
                var index = text.IndexOf(name, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                searchFrom = index + name.Length;
                if (IsWordBoundary(text, index - 1) && IsWordBoundary(text, index + name.Length) && !Overlaps(marks, index, name.Length))
                {
                    marks[index] = (name.Length, kind);
                }
            }
        }

        var segments = new List<EventTextSegment>();
        var position = 0;
        foreach (var (start, (length, kind)) in marks)
        {
            if (start > position)
            {
                segments.Add(new EventTextSegment(text[position..start], EventTextSegmentKind.PlainText));
            }

            segments.Add(new EventTextSegment(text.Substring(start, length), kind));
            position = start + length;
        }

        if (position < text.Length)
        {
            segments.Add(new EventTextSegment(text[position..], EventTextSegmentKind.PlainText));
        }

        return segments;
    }

    private static bool IsWordBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !(char.IsLetterOrDigit(text[index]) || text[index] == '_');

    private static bool Overlaps(SortedDictionary<int, (int Length, EventTextSegmentKind Kind)> marks, int start, int length) =>
        marks.Any(m => start < m.Key + m.Value.Length && m.Key < start + length);
}
