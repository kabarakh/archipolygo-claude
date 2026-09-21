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
    ConcernsMe,

    /// <summary>
    /// Show only item-send lines this group's own slots found for someone
    /// else (the mirror direction of a normal received item) - see
    /// <see cref="ViewModels.GroupViewModel.VisibleEvents"/> for how this is
    /// detected (the line's first <see cref="EventTextSegment"/>, i.e. the
    /// finder, classified as <see cref="EventTextSegmentKind.OwnSlotName"/>
    /// or <see cref="EventTextSegmentKind.ConnectedSlotName"/> by
    /// <see cref="Services.EventSegmentBuilder.BuildChatSegments"/>) -
    /// exclusive with <see cref="ConcernsMe"/>, not a separate independent
    /// toggle.
    /// </summary>
    FoundByMe
}
