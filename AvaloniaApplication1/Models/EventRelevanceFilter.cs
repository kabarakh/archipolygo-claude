namespace Archipolygo.Models;

/// <summary>
/// Filter applied to a tab's event log based on whether an entry concerns
/// this tab's own slot (see <see cref="EventEntry.ConcernsOwnSlot"/>).
/// </summary>
public enum EventRelevanceFilter
{
    /// <summary>Show every logged event, regardless of relevance.</summary>
    All,

    /// <summary>Show only events that concern this tab's own slot.</summary>
    ConcernsMe
}
