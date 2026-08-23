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
    /// Id of the <see cref="SlotProfile"/> this entry is specifically about
    /// (connect/disconnect, an item received, a hint concerning a configured
    /// slot), or null for genuinely room-wide entries - plain chat between
    /// players, which isn't tied to any one of this app's configured slots
    /// and should stay visible no matter which slot filter is selected.
    /// Since Phase 6, a <see cref="ViewModels.GroupViewModel"/> holds one
    /// merged event log for every slot configured on that server, so each
    /// slot-specific entry needs to say which slot it is about, for the slot
    /// filter dropdown to work.
    /// </summary>
    public Guid? SlotId { get; init; }

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

    /// <summary>
    /// Whether this entry actually concerns this tab's own slot, as opposed
    /// to e.g. chat banter between two other players, or a hint that's
    /// neither received by nor to be found by this slot. Used to decide
    /// whether the entry should count towards the tab's unread-event badge
    /// (see <see cref="ViewModels.GroupViewModel.UnreadEventCount"/>); defaults
    /// to true since most event types (connect/disconnect/error/item
    /// received) are inherently about this slot.
    /// </summary>
    public bool ConcernsOwnSlot { get; init; } = true;
}
