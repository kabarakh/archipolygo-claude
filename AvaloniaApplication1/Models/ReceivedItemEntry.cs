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

    /// <summary>
    /// Slot name of the <see cref="SlotProfile"/> identified by <see cref="SlotId"/>
    /// - i.e. which of this group's own configured slots the item belongs to.
    /// Shown alongside <see cref="SenderName"/>/<see cref="LocationName"/> because
    /// the items list is a merged, multi-slot list (see <see cref="EventEntry.SlotId"/>
    /// for why) and was historically always scoped to a single slot, so the owning
    /// slot was implicit before and now needs to be spelled out.
    /// </summary>
    public required string ReceivingSlotName { get; init; }

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

    /// <summary>
    /// True for items that were already in the slot's starting inventory -
    /// Archipelago surfaces these as the room-reserved "Server" player (see
    /// <c>ConnectionManager.FilterToRealPlayers</c>) finding the item at a
    /// "Server" location, which reads as noise; the finder/location line is
    /// replaced with "Starting Item" for these instead.
    /// </summary>
    public bool IsStartingItem =>
        string.Equals(SenderName, "Server", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LocationName, "Server", StringComparison.OrdinalIgnoreCase);
}
