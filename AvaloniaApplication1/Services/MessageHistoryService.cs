using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.Models;
using Avalonia.Threading;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

public class MessageHistoryService : IMessageHistoryService
{
    private readonly IPersistenceService _persistenceService;
    private readonly IProfileSyncStateStore _syncStateStore;
    private readonly int _eventHistoryLimit;

    public MessageHistoryService(IPersistenceService persistenceService, IProfileSyncStateStore syncStateStore)
    {
        _persistenceService = persistenceService;
        _syncStateStore = syncStateStore;
        // Read once at startup; a changed limit takes effect after a restart.
        _eventHistoryLimit = Math.Max(1, persistenceService.LoadSettings().EventHistoryLimit);
    }

    public void HandleConnected(TabViewModel tab)
    {
        var slotName = tab.ServerProfile.SlotName;
        AddEvent(tab, new EventEntry
        {
            Type = EventType.Connected,
            Text = $"Connected as {slotName}.",
            Segments = EventSegmentBuilder.BuildConnectedSegments(slotName)
        });
    }

    public void HandleDisconnected(TabViewModel tab, string reason)
    {
        var slotName = tab.ServerProfile.SlotName;
        AddEvent(tab, new EventEntry
        {
            Type = EventType.Disconnected,
            Text = $"Disconnected as {slotName} ({reason}).",
            Segments = EventSegmentBuilder.BuildDisconnectedSegments(slotName, reason)
        });
    }

    public void HandleError(TabViewModel tab, string message) =>
        AddEvent(tab, new EventEntry { Type = EventType.Error, Text = message });

    public void HandleChatMessage(TabViewModel tab, string text, IReadOnlyList<EventTextSegment> segments, EventType eventType = EventType.Chat)
    {
        // Only messages that actually name this tab's own slot (e.g. someone
        // sending/finding an item for/by it, or it being mentioned) should
        // count towards the unread badge - plain banter between other
        // players, or server messages that don't involve this slot at all,
        // should not.
        var concernsOwnSlot = false;
        foreach (var segment in segments)
        {
            if (segment.Kind == EventTextSegmentKind.OwnSlotName)
            {
                concernsOwnSlot = true;
                break;
            }
        }

        AddEvent(tab, new EventEntry { Type = eventType, Text = text, Segments = segments, ConcernsOwnSlot = concernsOwnSlot });
    }

    public void HandleItemsReceivedSinceLastConnection(TabViewModel tab, ServerProfile profile, ReadOnlyCollection<ItemInfo> allItemsReceived)
    {
        var syncState = _syncStateStore.Get(profile.Id);

        for (var i = syncState.LastSeenItemIndex; i < allItemsReceived.Count; i++)
        {
            var item = allItemsReceived[i];
            AddEvent(tab, new EventEntry
            {
                Type = EventType.ItemReceived,
                Text = $"Received {item.ItemDisplayName} ({item.LocationDisplayName}) since last connection",
                Segments = EventSegmentBuilder.BuildItemReceivedSegments(item.ItemDisplayName, item.Flags, item.LocationDisplayName),
                // Everything from the last persisted index onward is "new since last
                // session" by definition - these are items that arrived while this
                // client wasn't connected (or hadn't shown them yet).
                IsNewSinceLastSession = true
            });
        }

        AdvanceSyncState(syncState, allItemsReceived.Count);
    }

    public void AdvanceItemSyncState(ServerProfile profile, ReadOnlyCollection<ItemInfo> allItemsReceived)
    {
        // No log entry here on purpose: the live "X sent Y to Z" chat-log
        // line (see HandleChatMessage) already announces items received
        // while connected. Still need to keep the persisted index moving,
        // though, so a later reconnect doesn't re-announce these as "since
        // last connection".
        var syncState = _syncStateStore.Get(profile.Id);
        AdvanceSyncState(syncState, allItemsReceived.Count);
    }

    private void AdvanceSyncState(ProfileSyncState syncState, int newCount)
    {
        if (newCount > syncState.LastSeenItemIndex)
        {
            syncState.LastSeenItemIndex = newCount;
            _syncStateStore.Save(syncState);
        }
    }

    private void AddEvent(TabViewModel tab, EventEntry entry)
    {
        // Archipelago callbacks can arrive on a background/socket thread;
        // ObservableCollection mutations must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            tab.Events.Add(entry);

            while (tab.Events.Count > _eventHistoryLimit)
            {
                tab.Events.RemoveAt(0);
            }
        });
    }
}
