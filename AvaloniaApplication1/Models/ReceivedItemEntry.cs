using System;

namespace Archipolygo.Models;

/// <summary>
/// A single item a slot has received, shown in the received-items panel.
/// Immutable once created; the list itself grows as items arrive.
/// </summary>
public class ReceivedItemEntry
{
    /// <summary>
    /// Id of the <see cref="SlotProfile"/> this item was received by (see
    /// <see cref="EventEntry.SlotId"/> - same merged-list-plus-filter
    /// approach applies to the received-items list since Phase 6).
    /// </summary>
    public Guid SlotId { get; init; }

    public required string ItemName { get; init; }
    public required string LocationName { get; init; }

    /// <summary>The slot name of the player whose location check yielded this item.</summary>
    public required string SenderName { get; init; }

    /// <summary>
    /// Drives the item-name colour (progression / useful / trap / normal)
    /// via the shared <c>SegmentKindToBrushConverter</c>.
    /// </summary>
    public required EventTextSegmentKind ItemKind { get; init; }

    /// <summary>Drives the sender-name colour (own slot / connected slot / other).</summary>
    public required EventTextSegmentKind SenderKind { get; init; }
}
