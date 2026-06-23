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
    private readonly int _eventHistoryLimit;

    // Cached per profile so we don't hit disk on every single item/message.
    private readonly Dictionary<Guid, ProfileSyncState> _syncStateCache = new();

    public MessageHistoryService(IPersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
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

    public void HandleChatMessage(TabViewModel tab, string text, IReadOnlyList<EventTextSegment> segments) =>
        AddEvent(tab, new EventEntry { Type = EventType.Chat, Text = text, Segments = segments });

    public void HandleItemsReceived(TabViewModel tab, ServerProfile profile, ReadOnlyCollection<ItemInfo> allItemsReceived)
    {
        var syncState = GetOrLoadSyncState(profile.Id);

        for (var i = syncState.LastSeenItemIndex; i < allItemsReceived.Count; i++)
        {
            var item = allItemsReceived[i];
            AddEvent(tab, new EventEntry
            {
                Type = EventType.ItemReceived,
                Text = $"Received {item.ItemDisplayName} ({item.LocationDisplayName})",
                Segments = EventSegmentBuilder.BuildItemReceivedSegments(item.ItemDisplayName, item.Flags, item.LocationDisplayName),
                // Everything from the last persisted index onward is "new since last
                // session" by definition - this covers items received while offline
                // as well as items arriving live during the current session.
                IsNewSinceLastSession = true
            });
        }

        if (allItemsReceived.Count > syncState.LastSeenItemIndex)
        {
            syncState.LastSeenItemIndex = allItemsReceived.Count;
            _persistenceService.SaveSyncState(syncState);
        }
    }

    private ProfileSyncState GetOrLoadSyncState(Guid profileId)
    {
        if (!_syncStateCache.TryGetValue(profileId, out var state))
        {
            state = _persistenceService.LoadSyncState(profileId);
            _syncStateCache[profileId] = state;
        }

        return state;
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
