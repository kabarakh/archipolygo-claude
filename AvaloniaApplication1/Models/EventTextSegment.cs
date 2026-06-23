namespace Archipolygo.Models;

/// <summary>
/// One colorable run of text within an <see cref="EventEntry"/>'s log line,
/// e.g. a player's slot name or an item name. Immutable once created.
/// </summary>
public record EventTextSegment(string Text, EventTextSegmentKind Kind);
