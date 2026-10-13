namespace Archipolygo.Models;

/// <summary>One colorable run of text within an <see cref="EventEntry"/>'s log line.</summary>
public record EventTextSegment(string Text, EventTextSegmentKind Kind);
