using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Owns, per <see cref="ServerConnectionGroup"/>, at most one persistent
/// <c>ArchipelagoSession</c> at a time (the "leader" - see
/// <see cref="SwitchLeaderAsync"/>), plus occasional short-lived extra
/// sessions used only to catch a non-leader slot up on backlog it may have
/// missed (see <see cref="CatchUpSyncAsync"/>). Forwards incoming data to
/// <see cref="IMessageHistoryService"/> and <see cref="IHintService"/>.
/// </summary>
public interface IConnectionManager
{
    /// <summary>
    /// Raised whenever a leader switch or a manual disconnect changes a
    /// group's persisted <see cref="ServerConnectionGroup.AutoConnect"/>/
    /// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/> (see
    /// <see cref="SwitchLeaderAsync"/>/<see cref="DisconnectGroupAsync"/>),
    /// so the app can save groups to disk and this state survives a
    /// restart. Always raised on the UI thread.
    /// </summary>
    event Action<GroupViewModel>? GroupPersistNeeded;

    /// <summary>
    /// Raised once per configured slot as <see cref="InitializeGroupAsync"/>
    /// finishes processing it during app startup - whether that slot became
    /// the leader, was caught up via a brief non-leader connection, or that
    /// attempt failed (a failure still counts as "processed", so a stuck
    /// slot can't leave a progress indicator short forever). Deliberately
    /// only raised from <see cref="InitializeGroupAsync"/>'s own startup
    /// pass, not from <see cref="CatchUpSyncAsync"/> or
    /// <see cref="SwitchLeaderAsync"/> in general, so it doesn't also fire
    /// for unrelated later activity (e.g. "Add slot"). Always raised on the
    /// UI thread. Meant for a startup "catching up N/M slots" indicator.
    /// </summary>
    event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <summary>
    /// Makes <paramref name="targetSlot"/> the group's leader: connects it,
    /// and only once that succeeds, disconnects whichever slot was
    /// previously the leader (deliberately overlapping briefly, so the
    /// switch has no visible gap). If the group has no leader yet, this is
    /// just a normal connect. No-op if <paramref name="targetSlot"/> is
    /// already the leader. On success, also marks the group to reconnect
    /// this same slot automatically at the next app start (sets
    /// <see cref="ServerConnectionGroup.AutoConnect"/> and
    /// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>, raising
    /// <see cref="GroupPersistNeeded"/>) - connecting a leader by any means
    /// (the account dropdown, adding a new server's first slot, an
    /// unexpected-drop auto-reconnect) is itself what should make the group
    /// come back next time, not a separate opt-in.
    /// </summary>
    Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot);

    /// <summary>
    /// Drops the group's leader entirely; no slot in this group has an
    /// active connection afterwards. Does not touch any auto-reconnect state
    /// for other groups. Also clears <see cref="ServerConnectionGroup.AutoConnect"/>
    /// (raising <see cref="GroupPersistNeeded"/>) so this disconnect sticks
    /// across an app restart too, until the user reconnects manually.
    /// </summary>
    Task DisconnectGroupAsync(GroupViewModel group);

    /// <summary>
    /// One-shot: connects briefly as <paramref name="slot"/>, waits for
    /// login and its item/hint backlog to resync, then disconnects again.
    /// Never disconnects or reconnects the group's leader (if any) - the two
    /// sessions coexist for the few seconds this takes. Used when a new slot
    /// is added to a group that already has a leader, and once per
    /// non-leader slot at startup, to close gaps from time the app itself
    /// was closed.
    ///
    /// If a leader is already live, this also adds one more hint
    /// subscription to that existing session for <paramref name="slot"/>'s
    /// own numeric id (see ConnectionManager.TrackHintsForSiblingOnLeader) -
    /// without it, a slot added after the leader connected would stay
    /// invisible to the leader's ongoing hint tracking until the next leader
    /// reconnect, same gap as the one between already-configured siblings.
    /// </summary>
    Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot);

    /// <summary>
    /// Sends a chat message on the group's current leader session via
    /// <c>session.Say</c>. Does nothing if the group has no leader.
    /// </summary>
    Task SendMessageAsync(GroupViewModel group, string text);

    /// <summary>
    /// Called once per group at application startup. If
    /// <see cref="Models.ServerConnectionGroup.AutoConnect"/> is set,
    /// connects the preferred (or, failing that, first configured) slot as
    /// leader; then, regardless, runs a <see cref="CatchUpSyncAsync"/> pass
    /// for every other configured slot, one at a time, to close any gaps
    /// from the time the app itself was closed. Raises
    /// <see cref="SlotInitialSyncCompleted"/> exactly once per configured
    /// slot in this group as each one finishes.
    /// </summary>
    Task InitializeGroupAsync(GroupViewModel group);

    /// <summary>
    /// The current room's player roster, used to populate the "Add slot"
    /// dialog's picker so the user selects an existing player instead of
    /// typing a slot name blind. Prefers an already-open session for this
    /// group (the leader, or a catch-up dip already in flight); if none
    /// exists right now, briefly connects as whichever slot is already
    /// configured purely to read the roster, then disconnects again. Returns
    /// an empty list if the group has no configured slots yet, or if that
    /// brief connection attempt fails.
    /// </summary>
    Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group);
}
