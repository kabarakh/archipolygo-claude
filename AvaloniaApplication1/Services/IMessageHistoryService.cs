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
/// Converts raw data coming from an Archipelago session into <see cref="EventEntry"/>
/// instances and appends them to a group's merged event log. Also tracks, per
/// slot, which items have already been shown so that items received while
/// that slot wasn't the leader can be flagged as new the next time it becomes
/// the leader or gets a catch-up sync (see <see cref="ProfileSyncState"/>).
/// </summary>
public interface IMessageHistoryService
{
    void HandleConnected(GroupViewModel group, SlotProfile slot);

    void HandleDisconnected(GroupViewModel group, SlotProfile slot, string reason);

    void HandleError(GroupViewModel group, string message);

    /// <summary>
    /// <paramref name="segments"/> carries the same text split into colorable
    /// runs (player names etc.); pass an empty list if no further
    /// classification is available, in which case the entry falls back to
    /// plain text (see <see cref="EventEntry.EffectiveSegments"/>).
    /// </summary>
    /// <param name="slotId">
    /// Null for genuinely room-wide chat (not tied to any one configured
    /// slot); see <see cref="EventEntry.SlotId"/>.
    /// </param>
    /// <param name="eventType">
    /// Most messages on the session's <c>MessageLog</c> are plain chat, but
    /// some (item sends, item cheats) describe an item changing hands rather
    /// than something someone typed; pass <see cref="EventType.ItemReceived"/>
    /// for those so they show up under the "Item" log filter instead of
    /// "Chat" - see <see cref="Archipelago.MultiClient.Net.MessageLog.Messages.ItemSendLogMessage"/>.
    /// Defaults to <see cref="EventType.Chat"/>.
    /// </param>
    void HandleChatMessage(GroupViewModel group, string text, IReadOnlyList<EventTextSegment> segments, Guid? slotId, EventType eventType = EventType.Chat);

    /// <summary>
    /// Called with the items that were waiting on the server from before this
    /// connection attempt for <paramref name="slot"/> (i.e. received while it
    /// wasn't the leader, or otherwise not yet shown). Appends an
    /// <see cref="EventEntry"/> for every item beyond the last persisted
    /// index, marked as received since the last connection, and
    /// advances/saves that index. Only meant to be called once per
    /// successful connect/catch-up for a slot, before any live item arrives.
    /// </summary>
    void HandleItemsReceivedSinceLastConnection(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived);

    /// <summary>
    /// Called for items received live while <paramref name="slot"/> is the
    /// leader. The normal Archipelago "X sent Y to Z" chat-log line (see
    /// <see cref="HandleChatMessage"/>) already announces these, so this only
    /// advances/saves the persisted last-seen-item index for that slot.
    /// </summary>
    void AdvanceItemSyncState(SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived);

    /// <summary>
    /// Appends the most-recently-arrived item (the last element of
    /// <paramref name="allItemsReceived"/>) to the group's received-items
    /// panel, tagged for <paramref name="slot"/>. Called for every
    /// <c>ItemReceived</c> event on the leader's own session, regardless of
    /// whether the item is backlog or live.
    /// </summary>
    void TrackReceivedItem(GroupViewModel group, SlotProfile slot, ReadOnlyCollection<ItemInfo> allItemsReceived,
                           string senderName, EventTextSegmentKind senderKind);

    /// <summary>
    /// Appends a single item to the received-items panel for a configured
    /// slot that is <em>not</em> currently the leader, observed passively
    /// from the leader's chat/log broadcast (see <see cref="ConnectionManager"/>)
    /// rather than from that slot's own (nonexistent) session. Deliberately
    /// does not touch that slot's persisted <c>LastSeenItemIndex</c> - there
    /// is no authoritative running count to advance outside of a real login
    /// for that slot, so a later catch-up sync may harmlessly re-show this
    /// same item as "received since last connection" once more.
    /// </summary>
    void HandleObservedItemForSlot(GroupViewModel group, SlotProfile targetSlot, string itemDisplayName,
                                    string locationDisplayName, ItemFlags itemFlags,
                                    string senderName, EventTextSegmentKind senderKind);

    /// <summary>
    /// Clears only <paramref name="slot"/>'s entries from the group's
    /// received-items panel, in preparation for a fresh connection attempt
    /// for that slot (the server re-delivers that slot's full item history on
    /// every connect, so its portion of the list is rebuilt from scratch each
    /// time - other slots' entries in the same merged list are untouched).
    /// </summary>
    void ClearReceivedItemsForSlot(GroupViewModel group, Guid slotId);

    /// <summary>
    /// Appends an <see cref="EventEntry"/> for an incoming DeathLink (see
    /// Feature-Plaene/Archiv/DeathLink.md), using the new
    /// <see cref="EventType.DeathLink"/>. Room-wide like plain chat with no
    /// named configured slot - <see cref="EventEntry.SlotId"/> stays null,
    /// since a DeathLink concerns the whole room's DeathLink-tagged players,
    /// not specifically one of this app's own configured slots.
    /// </summary>
    void HandleDeathLinkReceived(GroupViewModel group, DeathLink deathLink);
}
