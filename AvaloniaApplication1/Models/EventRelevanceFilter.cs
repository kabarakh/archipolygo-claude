namespace Archipolygo.Models;

/// <summary>Filters a tab's event log by relevance to this group's own slots.</summary>
public enum EventRelevanceFilter
{
    All,

    /// <summary>Only events where <see cref="EventEntry.ConcernsOwnSlot"/> is set.</summary>
    ConcernsMe,

    /// <summary>
    /// Only item sends this group's own slots found for someone else (detected by the line's first segment,
    /// the finder, being an own/connected slot name). Exclusive with <see cref="ConcernsMe"/>.
    /// </summary>
    FoundByMe
}
