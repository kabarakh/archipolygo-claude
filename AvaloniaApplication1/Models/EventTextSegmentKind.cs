namespace Archipolygo.Models;

/// <summary>
/// Categorizes a single <see cref="EventTextSegment"/> so the UI can color it
/// without re-deriving the classification from raw Archipelago data.
/// </summary>
public enum EventTextSegmentKind
{
    /// <summary>No special meaning; rendered in the default text color.</summary>
    PlainText,

    /// <summary>The slot name of the profile this event log belongs to.</summary>
    OwnSlotName,

    /// <summary>
    /// A player's slot name where that player is currently being tracked by
    /// another tab of this app connected to the same Archipelago server
    /// instance (same host+port).
    /// </summary>
    ConnectedSlotName,

    /// <summary>Any other player's slot name.</summary>
    OtherSlotName,

    /// <summary>An item with the Trap flag.</summary>
    ItemTrap,

    /// <summary>An item with the Advancement (progression) flag.</summary>
    ItemProgression,

    /// <summary>An item with the NeverExclude (useful) flag.</summary>
    ItemUseful,

    /// <summary>Any other item (filler).</summary>
    ItemOther
}
