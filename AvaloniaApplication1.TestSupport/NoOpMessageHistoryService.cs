using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

/// <summary>Ignores every call so ConnectionManager tests don't need a real persistence/sync-state backing store.</summary>
public sealed class NoOpMessageHistoryService : IMessageHistoryService
{
    public void HandleConnected(GroupViewModel group, SlotProfile slot) { }
    public void HandleDisconnected(GroupViewModel group, SlotProfile slot, string reason) { }
    public void HandleError(GroupViewModel group, string message) { }
    public void HandleChatMessage(GroupViewModel group, string text, IReadOnlyList<EventTextSegment> segments, Guid? slotId, EventType eventType = EventType.Chat, RoomSlotStatus? receiver = null) { }
    public void HandleItemsReceivedSinceLastConnection(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived, RoomSlotStatus? receiver = null) { }
    public void AdvanceItemSyncState(SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived) { }
    public void TrackReceivedItem(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived, string senderName, EventTextSegmentKind senderKind) { }
    public void HandleObservedItemForSlot(GroupViewModel group, SlotProfile targetSlot, string itemDisplayName, string locationDisplayName, ItemFlags itemFlags, string senderName, EventTextSegmentKind senderKind, RoomSlotStatus? receiver = null) { }
    public void ClearReceivedItemsForSlot(GroupViewModel group, Guid slotId) { }
    public void HandleDeathLinkReceived(GroupViewModel group, DeathLink deathLink) { }
}
