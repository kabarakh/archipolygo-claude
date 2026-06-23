using System;
using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// A single entry in a connection tab's event log (connect/disconnect, items,
/// hints, chat, errors). Immutable once created.
/// </summary>
public class EventEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Text { get; init; } = string.Empty;

    public EventType Type { get; init; }

    /// <summary>
    /// True if this entry represents something that arrived since the last
    /// time this profile was connected (see <see cref="ProfileSyncState"/>).
    /// </summary>
    public bool IsNewSinceLastSession { get; init; }

    /// <summary>
    /// <see cref="Text"/> split into colorable runs (player names, item
    /// rarity, ...). Optional - leave empty for entries with no further
    /// classification (connect/disconnect/error); use <see cref="EffectiveSegments"/>
    /// for rendering, which falls back to a single plain-text run.
    /// </summary>
    public IReadOnlyList<EventTextSegment> Segments { get; init; } = Array.Empty<EventTextSegment>();

    /// <summary>
    /// <see cref="Segments"/> if set, otherwise a single plain-text segment
    /// wrapping <see cref="Text"/>. Use this for binding/rendering.
    /// </summary>
    public IReadOnlyList<EventTextSegment> EffectiveSegments =>
        Segments.Count > 0 ? Segments : new[] { new EventTextSegment(Text, EventTextSegmentKind.PlainText) };
}
