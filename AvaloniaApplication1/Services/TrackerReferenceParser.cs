using System;
using System.Linq;

namespace Archipolygo.Services;

/// <summary>Which kind of reference <see cref="TrackerReferenceParser.TryParseTrackerReference"/> recognized.</summary>
public enum TrackerReferenceKind
{
    TrackerId,

    /// <summary>Needs one more <c>/room_status/&lt;id&gt;</c> call to resolve into a tracker id.</summary>
    RoomId,
}

/// <summary>
/// Accepts a bare tracker SUUID, a ".../tracker/&lt;id&gt;" URL or a ".../room/&lt;id&gt;" URL. Only the last two
/// path segments are inspected, so self-hosted webhosts work too.
/// </summary>
public static class TrackerReferenceParser
{
    public static bool TryParseTrackerReference(string? input, out TrackerReferenceKind kind, out string value)
    {
        kind = default;
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        var suffixIndex = trimmed.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
        {
            trimmed = trimmed[..suffixIndex];
        }

        trimmed = trimmed.Trim('/');

        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!trimmed.Contains('/'))
        {
            // A real SUUID never contains whitespace; rejects obvious free text.
            if (trimmed.Any(char.IsWhiteSpace))
            {
                return false;
            }

            kind = TrackerReferenceKind.TrackerId;
            value = trimmed;
            return true;
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        var kindSegment = segments[^2];
        var idSegment = segments[^1];

        if (idSegment.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (string.Equals(kindSegment, "tracker", StringComparison.OrdinalIgnoreCase))
        {
            kind = TrackerReferenceKind.TrackerId;
            value = idSegment;
            return true;
        }

        if (string.Equals(kindSegment, "room", StringComparison.OrdinalIgnoreCase))
        {
            kind = TrackerReferenceKind.RoomId;
            value = idSegment;
            return true;
        }

        // E.g. ".../sphere_tracker/...": a validation error beats silently misinterpreting it.
        return false;
    }
}
