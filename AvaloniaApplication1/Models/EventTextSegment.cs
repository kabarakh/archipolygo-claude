namespace Archipolygo.Models;

/// <summary>
/// One colorable run of text within an <see cref="EventEntry"/>'s log line; <paramref name="Slot"/> is set
/// on player-name runs so the view can mark a slot that reached its goal.
/// </summary>
public record EventTextSegment(string Text, EventTextSegmentKind Kind, RoomSlotStatus? Slot = null);
