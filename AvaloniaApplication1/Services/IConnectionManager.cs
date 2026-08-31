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
    /// Raised once per configured slot as it finishes its part of a
    /// <see cref="SwitchLeaderAsync"/> call's sync pass - whether that slot
    /// became the leader itself, was caught up via a brief non-leader
    /// connection right after, or (a failed/skipped attempt still counts as
    /// "processed") never actually got a connection at all. Always paired
    /// with an earlier <see cref="SlotSyncBatchStarting"/> announcement that
    /// counted it, and always raised on the UI thread. Drives the
    /// "catching up N/M slots" progress indicator for every sync pass, not
    /// just the initial one at app startup - see <see cref="SlotSyncBatchStarting"/>.
    /// </summary>
    event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <summary>
    /// Raised right before <see cref="SwitchLeaderAsync"/> is about to
    /// attempt <paramref name="slotCount"/> slots' worth of connect/sync
    /// work as one batch - once for the leader connect itself (always 1),
    /// and again for the sibling catch-up pass right after, if the leader
    /// connected successfully and the group actually has other configured
    /// slots (only known at that point, hence the two separate
    /// announcements rather than one upfront total). Every slot counted in
    /// a raised batch is guaranteed a matching <see cref="SlotInitialSyncCompleted"/>
    /// afterward - success, failure, or skipped because a manual Disconnect
    /// cut the pass short - so a listener can safely track "total announced
    /// so far" vs. "total completed so far" as a simple running progress
    /// indicator that reappears for any later reconnect, not just the one
    /// startup pass. Always raised on the UI thread.
    /// </summary>
    event Action<int>? SlotSyncBatchStarting;

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
    ///
    /// If this connect is what actually brings the *group* online - i.e. no
    /// leader was live a moment ago - this also runs a fresh catch-up pass
    /// for every OTHER configured slot on the server, one at a time, from
    /// scratch, regardless of whether (or how far) an earlier connect for
    /// this same group got through its own pass. A same-group leader switch
    /// while the group was already online (e.g. the "Chat as" dropdown)
    /// does *not* re-run this pass - every other slot has already been kept
    /// current the whole time via the outgoing leader's own passive
    /// broadcast coverage, so there's nothing to catch up on.
    ///
    /// Either way, once this connect's group-wide sync pass (if any) is
    /// underway, it aborts immediately the moment the group's connection is
    /// interrupted - a manual Disconnect, or the leader itself dropping
    /// unexpectedly - rather than ploughing through the rest of the roster
    /// with nothing left to piggyback on; the next successful connect simply
    /// starts the whole pass over from the top for every slot, intentionally
    /// not just whichever ones a previous, interrupted pass happened to
    /// miss.
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
    /// is added to a group that already has a leader, and for every other
    /// configured slot right after a fresh leader connect (see
    /// <see cref="SwitchLeaderAsync"/>).
    ///
    /// Serialized against <see cref="SwitchLeaderAsync"/>/<see cref="DisconnectGroupAsync"/>
    /// via the same per-group gate those use, so this can never open a
    /// second connection while a sibling sweep or another catch-up for this
    /// group is already using its one-at-a-time connection slot - it simply
    /// waits its turn. No-ops immediately, without opening any connection,
    /// once its turn comes up, if: the group is currently in a
    /// manual-disconnect state (see <see cref="DisconnectGroupAsync"/>) -
    /// this is what actually stops a <see cref="SwitchLeaderAsync"/>
    /// sibling-sync pass partway through once the user hits Disconnect,
    /// since that pass just awaits this method once per remaining slot in a
    /// plain loop; or <paramref name="slot"/> has since been removed from
    /// the group's configuration - nothing left worth syncing for a slot
    /// that no longer exists by the time its queued turn actually arrives.
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
    /// Called once per group at application startup. A no-op - zero network
    /// activity, not even a brief catch-up dip - unless
    /// <see cref="Models.ServerConnectionGroup.AutoConnect"/> is set: a
    /// group the user left disconnected stays fully offline until they
    /// explicitly reconnect it. When it is set, connects the preferred (or,
    /// failing that, first configured) slot as leader via
    /// <see cref="SwitchLeaderAsync"/>, which itself then catches up every
    /// other configured slot as part of that same connect - see its doc
    /// comment.
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
