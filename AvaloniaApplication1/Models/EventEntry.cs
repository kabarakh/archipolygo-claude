using System;
using System.Collections.Generic;
using System.Linq;

namespace Archipolygo.Models;

/// <summary>A single, immutable entry in a tab's event log.</summary>
public class EventEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Text { get; init; } = string.Empty;

    public EventType Type { get; init; }

    /// <summary>
    /// The configured slot this entry is about, for the slot filter on the merged per-group log;
    /// null for room-wide entries (e.g. chat), which stay visible under every slot filter.
    /// </summary>
    public Guid? SlotId { get; init; }

    /// <summary>True if this arrived since the slot was last connected (see <see cref="ProfileSyncState"/>).</summary>
    public bool IsNewSinceLastSession { get; init; }

    /// <summary>Colorable runs of <see cref="Text"/>; may be empty. Render via <see cref="EffectiveSegments"/>.</summary>
    public IReadOnlyList<EventTextSegment> Segments { get; init; } = Array.Empty<EventTextSegment>();

    public IReadOnlyList<EventTextSegment> EffectiveSegments =>
        Segments.Count > 0 ? Segments : new[] { new EventTextSegment(Text, EventTextSegmentKind.PlainText) };

    /// <summary>Drives the "relevant only" filter; false for e.g. chat between other players or unrelated hints.</summary>
    public bool ConcernsOwnSlot { get; init; } = true;

    private static readonly EventTextSegmentKind[] ItemKinds =
    {
        EventTextSegmentKind.ItemProgression,
        EventTextSegmentKind.ItemUseful,
        EventTextSegmentKind.ItemOther,
        EventTextSegmentKind.ItemTrap
    };

    /// <summary>Item category of this entry's item segment, or null if it concerns no item (then item filters never hide it).</summary>
    public EventTextSegmentKind? ItemKind
    {
        get
        {
            foreach (var segment in Segments)
            {
                if (ItemKinds.Contains(segment.Kind))
                {
                    return segment.Kind;
                }
            }

            return null;
        }
    }
}
