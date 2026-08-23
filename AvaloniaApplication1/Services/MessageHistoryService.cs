using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

public class MessageHistoryService : IMessageHistoryService
{
    private readonly IProfileSyncStateStore _syncStateStore;
    private readonly int _eventHistoryLimit;

    public MessageHistoryService(IPersistenceService persistenceService, IProfileSyncStateStore syncStateStore)
    {
        _syncStateStore = syncStateStore;
        // Read once at startup; a changed limit takes effect after a restart.
        _eventHistoryLimit = Math.Max(1, persistenceService.LoadSettings().EventHistoryLimit);
    }

    public void HandleConnected(GroupViewModel group, SlotProfile slot)
    {
        var slotName = slot.SlotName;
        AddEvent(group, new EventEntry
        {
            SlotId = slot.Id,
            Type = EventType.Connected,
            Text = $"Connected as {slotName}.",
            Segments = EventSegmentBuilder.BuildConnectedSegments(slotName)
        });
    }

    public void HandleDisconnected(GroupViewModel group, SlotProfile slot, string reason)
    {
        var slotName = slot.SlotName;
        AddEvent(group, new EventEntry
        {
            SlotId = slot.Id,
            Type = EventType.Disconnected,
            Text = $"Disconnected as {slotName} ({reason}).",
            Segments = EventSegmentBuilder.BuildDisconnectedSegments(slotName, reason)
        });
    }

    public void HandleError(GroupViewModel group, string message) =>
        AddEvent(group, new EventEntry { Type = EventType.Error, Text = message });

    public void HandleChatMessage(GroupViewModel group, string text, IReadOnlyList<EventTextSegment> segments, Guid? slotId, EventType eventType = EventType.Chat)
    {
        // Only messages that actually name one of this group's configured
        // slots (e.g. someone sending/finding an item for/by it, or it being
        // mentioned) should count towards the unread badge - plain banter
        // between other players should not.
        var concernsOwnSlot = false;
        foreach (var segment in segments)
        {
            if (segment.Kind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName)
            {
                concernsOwnSlot = true;
                break;
            }
        }

        AddEvent(group, new EventEntry { SlotId = slotId, Type = eventType, Text = text, Segments = segments, ConcernsOwnSlot = concernsOwnSlot });
    }

    public void HandleItemsReceivedSinceLastConnection(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived)
    {
        var syncState = _syncStateStore.Get(slot.Id);

        for (var i = syncState.LastSeenItemIndex; i < allItemsReceived.Count; i++)
        {
            var item = allItemsReceived[i];
            AddEvent(group, new EventEntry
            {
                SlotId = slot.Id,
                Type = EventType.ItemReceived,
                Text = $"Received {item.ItemDisplayName} ({item.LocationDisplayName}) since last connection",
                Segments = EventSegmentBuilder.BuildItemReceivedSegments(item.ItemDisplayName, item.Flags, item.LocationDisplayName),
                // Everything from the last persisted index onward is "new since last
                // session" by definition - these are items that arrived while this
                // slot wasn't the leader (or hadn't shown them yet).
                IsNewSinceLastSession = true
            });
        }

        AdvanceSyncState(syncState, allItemsReceived.Count);
    }

    public void AdvanceItemSyncState(SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived)
    {
        // No log entry here on purpose: the live "X sent Y to Z" chat-log
        // line (see HandleChatMessage) already announces items received
        // while connected. Still need to keep the persisted index moving,
        // though, so a later reconnect doesn't re-announce these as "since
        // last connection".
        var syncState = _syncStateStore.Get(slot.Id);
        AdvanceSyncState(syncState, allItemsReceived.Count);
    }

    public void TrackReceivedItem(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived,
                                   string senderName, EventTextSegmentKind senderKind)
    {
        if (allItemsReceived.Count == 0)
            return;

        var item = allItemsReceived[allItemsReceived.Count - 1];
        var entry = new ReceivedItemEntry
        {
            SlotId            = slot.Id,
            ReceivingSlotName = slot.SlotName,
            ItemName          = item.ItemDisplayName,
            LocationName      = item.LocationDisplayName,
            SenderName        = senderName,
            ItemKind          = EventSegmentBuilder.ClassifyItemFlags(item.Flags),
            SenderKind        = senderKind,
        };

        Dispatcher.UIThread.Post(() => group.ReceivedItems.Add(entry));
    }

    public void HandleObservedItemForSlot(GroupViewModel group, SlotProfile targetSlot, string itemDisplayName,
                                           string locationDisplayName, ItemFlags itemFlags,
                                           string senderName, EventTextSegmentKind senderKind)
    {
        AddEvent(group, new EventEntry
        {
            SlotId = targetSlot.Id,
            Type = EventType.ItemReceived,
            Text = $"{senderName} sent {itemDisplayName} to {targetSlot.SlotName} ({locationDisplayName})",
            Segments = EventSegmentBuilder.BuildItemReceivedSegments(itemDisplayName, itemFlags, locationDisplayName),
            ConcernsOwnSlot = true
        });

        var entry = new ReceivedItemEntry
        {
            SlotId            = targetSlot.Id,
            ReceivingSlotName = targetSlot.SlotName,
            ItemName          = itemDisplayName,
            LocationName      = locationDisplayName,
            SenderName        = senderName,
            ItemKind          = EventSegmentBuilder.ClassifyItemFlags(itemFlags),
            SenderKind        = senderKind,
        };

        Dispatcher.UIThread.Post(() => group.ReceivedItems.Add(entry));
    }

    public void ClearReceivedItemsForSlot(GroupViewModel group, Guid slotId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            for (var i = group.ReceivedItems.Count - 1; i >= 0; i--)
            {
                if (group.ReceivedItems[i].SlotId == slotId)
                {
                    group.ReceivedItems.RemoveAt(i);
                }
            }
        });
    }

    private void AdvanceSyncState(ProfileSyncState syncState, int newCount)
    {
        if (newCount > syncState.LastSeenItemIndex)
        {
            syncState.LastSeenItemIndex = newCount;
            _syncStateStore.Save(syncState);
        }
    }

    private void AddEvent(GroupViewModel group, EventEntry entry)
    {
        // Archipelago callbacks can arrive on a background/socket thread;
        // ObservableCollection mutations must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            group.Events.Add(entry);

            while (group.Events.Count > _eventHistoryLimit)
            {
                group.Events.RemoveAt(0);
            }
        });
    }
}
