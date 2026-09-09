using System;
using System.Linq;

namespace Archipolygo.Services;

/// <summary>Which kind of reference <see cref="TrackerReferenceParser.TryParseTrackerReference"/> recognized.</summary>
public enum TrackerReferenceKind
{
    /// <summary>Already a usable tracker SUUID - either typed bare, or extracted from a "/tracker/&lt;id&gt;" URL.</summary>
    TrackerId,

    /// <summary>A room id, extracted from a "/room/&lt;id&gt;" URL - needs one more <c>/room_status/&lt;id&gt;</c> call to resolve into a tracker id.</summary>
    RoomId,
}

/// <summary>
/// Parses the single free-text field Fortschrittsanzeigen.md's Tier 2 (whole-
/// multiworld progress) accepts for a room's webhost tracker - see
/// Feature-Plaene/Archiv/Fortschrittsanzeigen.md, "Konsequenzen für das
/// Datenmodell", for the full rationale. Accepts, in any combination of
/// scheme/trailing-slash/query-or-fragment suffix:
/// <list type="bullet">
/// <item>a bare tracker SUUID ("2gVkMQgISGScA8wsvDZg5A")</item>
/// <item>a tracker URL ("https://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A")</item>
/// <item>a room URL ("https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg")</item>
/// </list>
/// Recognition is domain-independent (only the last two path segments are
/// inspected), so this works the same for a self-hosted webhost instance as
/// for archipelago.gg. Pure string logic, no network access - see
/// <see cref="Services.IMultiworldTrackerService.ResolveTrackerIdAsync"/> for
/// what actually turns a resolved <see cref="TrackerReferenceKind.RoomId"/>
/// into a tracker id.
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
            // No slash at all - not a URL or path, just a bare id. A real
            // SUUID never contains whitespace; this rejects obvious garbled
            // free text ("wirres Freitext") without rejecting a legitimate
            // id this parser can't otherwise validate the shape of.
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

        // Anything else (e.g. ".../sphere_tracker/...") isn't a supported
        // form - a validation error in the UI beats silently misinterpreting it.
        return false;
    }
}
