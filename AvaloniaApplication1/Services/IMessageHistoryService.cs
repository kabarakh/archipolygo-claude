using System.Collections.Generic;
using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Converts raw data coming from an Archipelago session into <see cref="EventEntry"/>
/// instances and appends them to a tab's event log. Also tracks, per profile,
/// which items have already been shown so that items received while offline
/// can be flagged as new on the next login (see <see cref="ProfileSyncState"/>).
/// </summary>
public interface IMessageHistoryService
{
    void HandleConnected(TabViewModel tab);

    void HandleDisconnected(TabViewModel tab, string reason);

    void HandleError(TabViewModel tab, string message);

    /// <summary>
    /// <paramref name="segments"/> carries the same text split into colorable
    /// runs (player names etc.); pass an empty list if no further
    /// classification is available, in which case the entry falls back to
    /// plain text (see <see cref="EventEntry.EffectiveSegments"/>).
    /// </summary>
    /// <param name="eventType">
    /// Most messages on the session's <c>MessageLog</c> are plain chat, but
    /// some (item sends, item cheats) describe an item changing hands rather
    /// than something someone typed; pass <see cref="EventType.ItemReceived"/>
    /// for those so they show up under the "Item" log filter instead of
    /// "Chat" - see <see cref="Archipelago.MultiClient.Net.MessageLog.Messages.ItemSendLogMessage"/>.
    /// Defaults to <see cref="EventType.Chat"/>.
    /// </param>
    void HandleChatMessage(TabViewModel tab, string text, IReadOnlyList<EventTextSegment> segments, EventType eventType = EventType.Chat);

    /// <summary>
    /// Called with the items that were waiting on the server from before this
    /// connection attempt (i.e. received while offline, or otherwise not yet
    /// shown to this client). Appends an <see cref="EventEntry"/> for every
    /// item beyond the last persisted index, marked as received since the
    /// last connection, and advances/saves that index. Only meant to be
    /// called once per successful connect/reconnect, before any live item
    /// arrives - see <see cref="AdvanceItemSyncState"/> for those.
    /// </summary>
    void HandleItemsReceivedSinceLastConnection(TabViewModel tab, ServerProfile profile, ReadOnlyCollection<ItemInfo> allItemsReceived);

    /// <summary>
    /// Called for items received live while already connected. The normal
    /// Archipelago "X sent Y to Z" chat-log line (see
    /// <see cref="HandleChatMessage"/>) already announces these, so this only
    /// advances/saves the persisted last-seen-item index - it does not append
    /// another log entry - to make sure a later reconnect doesn't re-announce
    /// items that were already shown live.
    /// </summary>
    void AdvanceItemSyncState(ServerProfile profile, ReadOnlyCollection<ItemInfo> allItemsReceived);
}
