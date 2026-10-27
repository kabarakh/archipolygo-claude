using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Turns session data into <see cref="EventEntry"/> instances and tracks per-slot item sync
/// state (<see cref="ProfileSyncState"/>) so items missed while not leader get flagged.
/// </summary>
public interface IMessageHistoryService
{
    void HandleConnected(GroupViewModel group, SlotProfile slot);

    void HandleDisconnected(GroupViewModel group, SlotProfile slot, string reason);

    void HandleError(GroupViewModel group, string message);

    /// <param name="segments">Empty falls back to plain text.</param>
    /// <param name="slotId">Null for room-wide chat.</param>
    /// <param name="eventType">
    /// <see cref="EventType.ItemReceived"/> for item sends/cheats on the MessageLog, so they filter as "Item".
    /// </param>
    /// <param name="receiver">Receiving slot of an item send or hint line; null for every other line.</param>
    void HandleChatMessage(GroupViewModel group, string text, IReadOnlyList<EventTextSegment> segments, Guid? slotId, EventType eventType = EventType.Chat,
                           RoomSlotStatus? receiver = null);

    /// <summary>
    /// Appends items beyond the last persisted index as "received since last connection". Call once
    /// per successful connect/catch-up, before any live item arrives.
    /// </summary>
    void HandleItemsReceivedSinceLastConnection(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived,
                                                RoomSlotStatus? receiver = null);

    /// <summary>
    /// Only advances the persisted last-seen index; the "X sent Y to Z" chat line already announces live items.
    /// </summary>
    void AdvanceItemSyncState(SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived);

    /// <summary>Appends the last element of <paramref name="allItemsReceived"/>, backlog or live.</summary>
    void TrackReceivedItem(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived,
                           string senderName, EventTextSegmentKind senderKind);

    /// <summary>
    /// For a non-leader slot observed via the leader's broadcast. Deliberately leaves its
    /// <c>LastSeenItemIndex</c> alone (no authoritative count without a login), so a later
    /// catch-up may harmlessly re-show this item.
    /// </summary>
    void HandleObservedItemForSlot(GroupViewModel group, SlotProfile targetSlot, string itemDisplayName,
                                    string locationDisplayName, ItemFlags itemFlags,
                                    string senderName, EventTextSegmentKind senderKind, RoomSlotStatus? receiver = null);

    /// <summary>
    /// Clears only <paramref name="slot"/>'s received items - the server re-delivers a slot's full
    /// item history on every connect.
    /// </summary>
    void ClearReceivedItemsForSlot(GroupViewModel group, Guid slotId);

    /// <summary>Room-wide: <see cref="EventEntry.SlotId"/> stays null.</summary>
    void HandleDeathLinkReceived(GroupViewModel group, DeathLink deathLink);
}
