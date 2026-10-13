using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Owns at most one persistent leader session per <see cref="ServerConnectionGroup"/>,
/// plus short-lived extra sessions for catch-up and Hint-picker probes. Forwards
/// incoming data to <see cref="IMessageHistoryService"/> and <see cref="IHintService"/>.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Raised when a leader switch or disconnect changed the group's persisted
    /// <see cref="ServerConnectionGroup.AutoConnect"/>/<see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>
    /// and groups should be saved. Raised on the UI thread.
    /// </summary>
    event Action<GroupViewModel>? GroupPersistNeeded;

    /// <summary>
    /// Raised once per slot counted in an earlier <see cref="SlotSyncBatchStarting"/>,
    /// when it finished its part of a sync pass (success, failure or skipped all count).
    /// Raised on the UI thread.
    /// </summary>
    event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <summary>
    /// Raised before <see cref="SwitchLeaderAsync"/> processes the given number of slots
    /// as one batch (the leader connect, then the sibling catch-up); each counted slot gets exactly
    /// one <see cref="SlotInitialSyncCompleted"/>. Raised on the UI thread.
    /// </summary>
    event Action<int>? SlotSyncBatchStarting;

    /// <summary>
    /// Leader-session server text that may answer a command this app sent (see
    /// <see cref="ServerReplyKind"/>); the server never correlates replies. Raised on the UI
    /// thread, in arrival order.
    /// </summary>
    event Action<GroupViewModel, ServerReplyKind, string>? ServerReplyReceived;

    /// <summary>
    /// Raised when <see cref="SwitchLeaderAsync"/> made a slot the live leader (a new socket, so
    /// per-connection server state such as "!admin login" is gone). Raised on the UI thread, after
    /// <see cref="GroupViewModel.LeaderSlotId"/> is updated.
    /// </summary>
    event Action<GroupViewModel>? LeaderConnected;

    /// <summary>
    /// Single-subscriber hook asking the UI for a password during any connect attempt: when one is
    /// required but empty (<c>isRetryAfterFailure</c> false) or the last one was rejected (true).
    /// Return <c>true</c> after updating the slot/group password to retry, <c>false</c> to give up
    /// quietly (state settles to <see cref="ConnectionState.Disconnected"/>). <c>null</c> = never prompt.
    /// </summary>
    Func<GroupViewModel, SlotProfile, bool, CancellationToken, Task<bool>>? PasswordRequested { get; set; }

    /// <summary>
    /// Makes <paramref name="targetSlot"/> the leader: connects it, then disconnects the previous
    /// leader (no-op if already leader). On success sets AutoConnect/PreferredLeaderSlotId and raises
    /// <see cref="GroupPersistNeeded"/>. If this brings the group online, also catches up every other
    /// slot one at a time, aborting if the connection is interrupted.
    /// </summary>
    Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot);

    /// <summary>
    /// Drops the group's leader so no slot is connected, and clears
    /// <see cref="ServerConnectionGroup.AutoConnect"/> (raising <see cref="GroupPersistNeeded"/>).
    /// </summary>
    Task DisconnectGroupAsync(GroupViewModel group);

    /// <summary>
    /// Briefly connects as <paramref name="slot"/> to resync its item/hint backlog, then disconnects;
    /// the leader is never touched. Serialized on the group's gate; no-op if the group is manually
    /// disconnected or the slot was removed. Also adds the slot to the live leader's hint tracking.
    /// </summary>
    Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot);

    /// <summary>
    /// Sends a chat message on the group's leader session. Does nothing if there is no leader.
    /// </summary>
    Task SendMessageAsync(GroupViewModel group, string text);

    /// <summary>
    /// Startup entry point per group: no network activity unless
    /// <see cref="Models.ServerConnectionGroup.AutoConnect"/> is set, otherwise connects the preferred
    /// (or first) slot via <see cref="SwitchLeaderAsync"/>.
    /// </summary>
    Task InitializeGroupAsync(GroupViewModel group);

    /// <summary>
    /// The room's player roster, from an open session or a brief temporary connection as a configured
    /// slot. Empty if the group has no slots or the connection fails.
    /// </summary>
    Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group);

    /// <summary>
    /// <paramref name="slot"/>'s not-yet-found locations, from cache if it has connected since app
    /// start, otherwise via a kept-alive probe connection (see <see cref="ReleaseHeldSessionAsync"/>).
    /// Serialized on the group's gate. Empty if no connection could be made; already-hinted
    /// locations are not excluded.
    /// </summary>
    Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group, SlotProfile slot);

    /// <summary>
    /// Hints <paramref name="locationId"/> for <paramref name="slot"/>, with the same session reuse/probe
    /// behavior as <see cref="GetHintableLocationsAsync"/>. No-op if no connection could be made.
    /// </summary>
    Task SendHintAsync(GroupViewModel group, SlotProfile slot, long locationId);

    /// <summary>
    /// Sends <c>!hint &lt;itemName&gt;</c> as <paramref name="slot"/>, with the same session reuse/probe
    /// behavior as <see cref="GetHintableLocationsAsync"/>. No-op if no connection or a blank name.
    /// </summary>
    Task SendItemHintAsync(GroupViewModel group, SlotProfile slot, string itemName);

    /// <summary>
    /// Disconnects a non-leader probe session kept alive for <paramref name="slot"/> by the Hint picker
    /// methods. No-op if the slot is the leader or nothing is held.
    /// </summary>
    Task ReleaseHeldSessionAsync(GroupViewModel group, SlotProfile slot);

    /// <summary>
    /// Every item name <paramref name="slot"/>'s game defines (DataPackage, not just this seed), via any
    /// live session or else a kept-alive probe as <paramref name="slot"/>. Empty on failure; already
    /// found/hinted names are not excluded.
    /// </summary>
    Task<IReadOnlyList<string>> GetHintableItemsAsync(GroupViewModel group, SlotProfile slot);

    /// <summary>
    /// Every item and location name <paramref name="game"/> defines, via the leader session only
    /// (shares <see cref="GetHintableItemsAsync"/>'s cache). Null if no leader or the request fails.
    /// </summary>
    Task<GameDataNames?> GetGameDataAsync(GroupViewModel group, string game);

    /// <summary>
    /// The room settings the leader session currently reports (kept current by RoomUpdate).
    /// Null if the group has no leader session.
    /// </summary>
    RoomSettingsSnapshot? GetRoomSettings(GroupViewModel group);
}
