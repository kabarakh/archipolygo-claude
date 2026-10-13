namespace Archipolygo.Models;

/// <summary>Classifies an <see cref="EventTextSegment"/> so the UI can color it without the raw Archipelago data.</summary>
public enum EventTextSegmentKind
{
    PlainText,

    /// <summary>The slot name of the profile this event log belongs to.</summary>
    OwnSlotName,

    /// <summary>Another of this app's configured slots on the same server (same host+port).</summary>
    ConnectedSlotName,

    OtherSlotName,

    /// <summary>Item with the Trap flag.</summary>
    ItemTrap,

    /// <summary>Item with the Advancement flag.</summary>
    ItemProgression,

    /// <summary>Item with the NeverExclude flag.</summary>
    ItemUseful,

    /// <summary>Any other item (filler).</summary>
    ItemOther,

    DeathLink
}
