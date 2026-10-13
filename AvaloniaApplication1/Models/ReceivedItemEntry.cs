using System;

namespace Archipolygo.Models;

/// <summary>A single, immutable item a configured slot has received.</summary>
public class ReceivedItemEntry
{
    public Guid SlotId { get; init; }

    /// <summary>Which configured slot got the item - shown because the items list merges every slot of the group.</summary>
    public required string ReceivingSlotName { get; init; }

    public required string ItemName { get; init; }
    public required string LocationName { get; init; }

    /// <summary>The slot name of the player whose location check yielded this item.</summary>
    public required string SenderName { get; init; }

    public required EventTextSegmentKind ItemKind { get; init; }

    public required EventTextSegmentKind SenderKind { get; init; }

    /// <summary>
    /// Starting-inventory items arrive as found by the reserved "Server" player at a "Server" location;
    /// the UI shows "Starting Item" instead of that noise.
    /// </summary>
    public bool IsStartingItem =>
        string.Equals(SenderName, "Server", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LocationName, "Server", StringComparison.OrdinalIgnoreCase);
}
