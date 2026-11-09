namespace Archipolygo.Models;

/// <summary>One entry of the webhost tracker's <c>player_items_received</c>: Archipelago's NetworkItem, ids only.</summary>
public sealed record TrackerReceivedItem(long ItemId, long LocationId, int SenderSlot, int Flags);
