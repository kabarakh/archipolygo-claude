using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// Owns, per <see cref="ServerConnectionGroup"/>, at most one persistent
/// <see cref="ArchipelagoSession"/> at a time (the "leader") plus occasional
/// short-lived extra sessions used only to catch a non-leader slot up on
/// backlog (see <see cref="CatchUpSyncAsync"/>). Wires session events to
/// <see cref="IMessageHistoryService"/> and <see cref="IHintService"/>.
///
/// Phase 6 rewrite: previously one <see cref="ArchipelagoSession"/> existed
/// per profile/tab, all of them independent and simultaneous. Now a server
/// with several configured slots keeps only one live connection at a time
/// (the "leader"); switching which slot is the leader is the only action
/// that opens/closes a socket. Non-leader slots stay current passively:
/// item-send and hint broadcasts on the leader's session already cover the
/// whole room (see <see cref="OnLeaderMessageReceived"/> and
/// <see cref="OnHintsUpdated"/>), and a brief "catch-up" connection (see
/// <see cref="CatchUpSyncAsync"/>) closes whatever gap accumulated while no
/// slot in the group was connected at all - run for every other configured
/// slot only when a leader connect actually brings the *group* online (see
/// <see cref="SwitchLeaderAsync"/>'s <c>previousLeaderId is null</c> check),
/// never on any kind of timer or opportunistic schedule while the group is
/// actually disconnected, and never again for a same-group leader switch
/// (e.g. the "Chat as" dropdown) while the group was already online, since
/// every sibling slot already stayed current the whole time via the
/// outgoing leader's own passive broadcast coverage. See Umsetzungsplan.md,
/// Phase 6, for the full design rationale.
/// </summary>
public class ConnectionManager : IConnectionManager
{
    // Backoff schedule for leader auto-reconnect attempts; the last value repeats for further attempts.
    private static readonly int[] ReconnectDelaysSeconds = { 5, 10, 30 };

    // Spacing between the *start* of two consecutive groups' startup connect
    // attempts, so opening the app with several AutoConnect servers doesn't
    // hit them all with a burst of simultaneous handshakes. See
    // MainWindowViewModel, which awaits InitializeGroupAsync per group with
    // this delay between calls.
    public static readonly TimeSpan StartupGroupSpacing = TimeSpan.FromSeconds(3);

    // How long to keep treating newly received items as backlog rather than
    // live receipts after a successful leader login (see original rationale
    // below), and how long a catch-up session waits after login before
    // closing itself, to make sure the backlog burst has fully landed.
    //
    // The server sends the initial ReceivedItems backlog as a separate
    // WebSocket message after the Connected packet, so LoginAsync's task can
    // resolve (and our async continuation can run) before all backlog items
    // have fired ItemReceived. Setting hasAnnouncedConnection = true
    // immediately would race against the socket thread still delivering
    // those items - the continuation could flip the flag between two
    // consecutive ItemReceived calls inside the same burst, silently
    // suppressing everything after the first item. A brief delay ensures the
    // entire synchronous burst finishes first. Two seconds is generous; in
    // practice the burst completes in milliseconds.
    private static readonly TimeSpan ItemBacklogGracePeriod = TimeSpan.FromSeconds(2);

    // The Archipelago network-protocol version this client implements (used
    // in the login handshake) - not this app's own version number.
    private static readonly Version ArchipelagoProtocolVersion = new(0, 6, 7);

    // How many times ConnectSlotSessionAsync will attempt a connect+login
    // that keeps failing with what looks like a transient hiccup (see
    // IsTransientConnectFailure) before giving up and reporting it as a real
    // failure - 1 initial attempt plus this many retries.
    private const int MaxTransientConnectRetries = 2;

    // Pause between a transient-looking failed attempt and the next retry -
    // long enough to let a one-off network/timing blip clear, short enough
    // that a genuinely broken connection still fails within a few seconds.
    private static readonly TimeSpan TransientConnectRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;

    // Keyed by SlotProfile.Id. A session exists here while that slot has ANY
    // active connection - as the group's leader, or as a short-lived
    // catch-up/switch-target session in flight. At most two entries can
    // exist for the same group at once (old leader + new leader during a
    // switch, or leader + one catch-up dip), and only ever briefly.
    private readonly ConcurrentDictionary<Guid, ArchipelagoSession> _sessions = new();

    // GroupId -> the SlotProfile.Id that currently holds the persistent
    // leader connection. Absent = the group has no leader right now.
    private readonly ConcurrentDictionary<Guid, Guid> _leaderSlotByGroup = new();

    // GroupId -> a lock serializing SwitchLeaderAsync/DisconnectGroupAsync
    // for that group, so a switch and a disconnect (or two switches) can
    // never race each other for the same server.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _groupLocks = new();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reconnectTokens = new();

    // GroupId -> the CancellationTokenSource for whichever single connect
    // attempt (SwitchLeaderAsync's leader connect, or CatchUpSyncAsync's
    // catch-up dip - never both at once, per the "slots never connect
    // concurrently" invariant) is currently in flight for that group.
    // DisconnectGroupAsync cancels this *before* it waits on _groupLocks, so
    // a manual disconnect actually interrupts a stuck/retrying connect
    // instead of silently queuing behind it until the retries exhaust
    // themselves - see DisconnectGroupAsync and ConnectSlotSessionAsync.
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _connectCancellationSources = new();

    // Set right before a deliberate DisconnectGroupAsync and only cleared
    // again by a subsequent explicit SwitchLeaderAsync (account dropdown,
    // startup). Deliberately not consumed/removed by OnSocketClosed, mirroring
    // the pre-Phase-6 design: some socket implementations fire SocketClosed
    // more than once for a single deliberate close, and a one-shot flag would
    // only suppress the first of those.
    private readonly ConcurrentDictionary<Guid, bool> _autoReconnectSuppressed = new();

    /// <inheritdoc/>
    public event Action<GroupViewModel>? GroupPersistNeeded;

    /// <inheritdoc/>
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <inheritdoc/>
    public event Action<int>? SlotSyncBatchStarting;

    public ConnectionManager(IMessageHistoryService messageHistoryService, IHintService hintService)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
    }

    public async Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot)
    {
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_leaderSlotByGroup.TryGetValue(groupId, out var currentLeaderId) && currentLeaderId == targetSlot.Id)
            {
                return; // already the leader - nothing to do.
            }

            // A manual switch always wins over a pending auto-reconnect loop
            // for this group, and lifts a previous manual disconnect's
            // suppression so auto-reconnect can take effect again later.
            CancelPendingReconnect(groupId);
            _autoReconnectSuppressed.TryRemove(groupId, out _);

            SetConnectionState(group, ConnectionState.Connecting);

            // Announce just the leader attempt for now (see
            // SlotSyncBatchStarting's doc comment) - whether it succeeds and
            // the sibling catch-up pass below actually happens is still
            // unknown at this point, so the progress indicator this drives
            // shouldn't claim more work than is guaranteed to be attempted.
            RaiseSlotSyncBatchStarting(1);

            // Connect the new leader FIRST, and only close the old one once
            // that succeeds - a deliberate brief overlap (both sessions are
            // live for a moment) so the switch has no visible gap, at the
            // cost of a chat message that arrives in exactly that window
            // possibly not showing up in the log (see Umsetzungsplan.md).
            using var connectCts = new CancellationTokenSource();
            _connectCancellationSources[groupId] = connectCts;
            ArchipelagoSession? newSession;
            try
            {
                newSession = await ConnectSlotSessionAsync(group, targetSlot, isLeaderSession: true, connectCts.Token);
            }
            finally
            {
                _connectCancellationSources.TryRemove(groupId, out _);
            }

            if (newSession is null)
            {
                // Already reported via _messageHistoryService.HandleError and
                // ConnectionState.Error inside ConnectSlotSessionAsync. The
                // previous leader (if any) was never touched, so restore the
                // dropdown/leader bookkeeping to reflect that instead of
                // leaving it pointing at a slot that never actually connected.
                var stillLeaderId = _leaderSlotByGroup.TryGetValue(groupId, out var stillLeader) ? (Guid?)stillLeader : null;
                var stillLeaderSlot = stillLeaderId is null ? null : FindSlot(group, stillLeaderId.Value);
                Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(stillLeaderId, stillLeaderSlot));

                // Closes out the 1-slot batch announced above even though it
                // failed - a failed attempt still counts as "processed" (see
                // SlotInitialSyncCompleted's doc comment), so the progress
                // indicator isn't left stuck short forever. No siblings were
                // ever attempted in this case - a failed leader connect
                // means the group still isn't actually online.
                RaiseSlotInitialSyncCompleted(group, targetSlot);
                return;
            }

            var previousLeaderId = _leaderSlotByGroup.TryGetValue(groupId, out var prev) ? (Guid?)prev : null;
            _leaderSlotByGroup[groupId] = targetSlot.Id;
            Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(targetSlot.Id, targetSlot));
            SetConnectionState(group, ConnectionState.Connected);

            // A successful leader connection - by any means: the account
            // dropdown, a brand-new server's first slot, or an
            // unexpected-drop auto-reconnect - is itself what should bring
            // this group back at the next app start, so it's remembered
            // here rather than requiring a separate "Auto-connect" opt-in.
            // Posted to the UI thread like every other group mutation above,
            // both because AutoConnect/PreferredLeaderSlotId are themselves
            // observable properties and so that GroupPersistNeeded's
            // subscriber can safely enumerate the Groups collection.
            if (!group.Group.AutoConnect || group.Group.PreferredLeaderSlotId != targetSlot.Id)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    group.Group.AutoConnect = true;
                    group.Group.PreferredLeaderSlotId = targetSlot.Id;
                    GroupPersistNeeded?.Invoke(group);
                });
            }

            if (previousLeaderId is not null && previousLeaderId != targetSlot.Id &&
                _sessions.TryRemove(previousLeaderId.Value, out var oldSession))
            {
                var previousSlot = FindSlot(group, previousLeaderId.Value);
                if (previousSlot is not null)
                {
                    _messageHistoryService.HandleDisconnected(group, previousSlot, "switched account");
                }

                await TryCloseSocketAsync(group, previousSlot, oldSession);
            }

            // The leader itself just got its own full backlog via login;
            // report it done right away rather than waiting on the sibling
            // loop below (see SlotInitialSyncCompleted's doc comment - this
            // is what feeds the startup "Catching up slots: N/M" banner).
            RaiseSlotInitialSyncCompleted(group, targetSlot);

            // Every OTHER configured slot on this server gets a brief
            // catch-up dip too - but only when this connect is what's
            // actually bringing the *group* online (previousLeaderId is
            // null: InitializeGroupAsync's startup connect, or a manual
            // reconnect after DisconnectGroupAsync/an unexpected drop). A
            // disconnected group (no leader) gets zero network activity at
            // all (see InitializeGroupAsync), so the moment it actually
            // connects is exactly when "make every configured slot current"
            // needs to happen, in one go, from scratch - simpler and more
            // predictable than trying to track and resume only whatever a
            // previous, possibly-interrupted pass happened to skip.
            //
            // When the group was already online (previousLeaderId is not
            // null - this is just an account switch via the "Chat as"
            // dropdown), every other configured slot has already been kept
            // current the whole time via the outgoing leader's passive
            // broadcast coverage (see OnLeaderMessageReceived/OnHintsUpdated) -
            // there's no gap to close, so re-syncing everyone here on every
            // single account switch would just be a redundant round of
            // logins with nothing new to show for it.
            if (previousLeaderId is not null)
            {
                return;
            }

            var siblingSlots = group.Group.Slots.Where(s => s.Id != targetSlot.Id).ToList();
            if (siblingSlots.Count == 0)
            {
                return;
            }

            // Only now that the leader has actually connected does this
            // pass's full size become known - see SlotSyncBatchStarting's
            // doc comment for why the leader's own attempt was announced
            // separately, before this point.
            RaiseSlotSyncBatchStarting(siblingSlots.Count);

            for (var i = 0; i < siblingSlots.Count; i++)
            {
                var siblingSlot = siblingSlots[i];
                await CatchUpSyncCoreAsync(group, siblingSlot);
                RaiseSlotInitialSyncCompleted(group, siblingSlot);

                // Abort the rest of the sweep the moment this group's
                // connection was interrupted mid-pass - either a manual
                // Disconnect (_autoReconnectSuppressed, set synchronously by
                // DisconnectGroupAsync before it even tries to acquire this
                // same gate - see its own doc comment for why) or the leader
                // itself dropping unexpectedly (OnSocketClosed removes it
                // from _leaderSlotByGroup synchronously too, from the
                // socket's own event thread, without waiting on this gate).
                // Either way there's no group connection left for the
                // remaining siblings to piggyback on, so ploughing on would
                // just open brand-new, pointless connections one after
                // another. The next successful leader connect runs this
                // whole sweep again from the top for every configured slot,
                // rather than trying to resume only whatever got skipped.
                var stillOnline = _leaderSlotByGroup.TryGetValue(groupId, out var stillLeaderId) && stillLeaderId == targetSlot.Id;
                if (_autoReconnectSuppressed.ContainsKey(groupId) || !stillOnline)
                {
                    // Every slot this loop never got to still needs its
                    // "processed" signal though, so the progress indicator
                    // this pass announced a size for isn't left stuck short
                    // forever just because the pass stopped early.
                    for (var skipped = i + 1; skipped < siblingSlots.Count; skipped++)
                    {
                        RaiseSlotInitialSyncCompleted(group, siblingSlots[skipped]);
                    }

                    return;
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectGroupAsync(GroupViewModel group)
    {
        var groupId = group.Group.Id;

        // Both of these run synchronously, *before* ever touching
        // _groupLocks - and in this order. SwitchLeaderAsync/CatchUpSyncAsync
        // hold that lock for the entire duration of their connect (including
        // every transient-failure retry), so waiting for the lock first
        // would just queue this call silently behind a stuck/retrying
        // connect until it gives up on its own; cancelling first is what
        // makes Disconnect actually interrupt it instead, e.g. right after
        // app startup while a group's server is unreachable and its leader
        // connect is still retrying. Setting the suppression flag *before*
        // cancelling (rather than later, inside the gated body below) closes
        // a race with InitializeGroupAsync's own "did the user just
        // disconnect this group?" check: that check only runs after the
        // in-flight SwitchLeaderAsync call has observed the cancellation and
        // returned, which can only happen after this line, so the flag is
        // guaranteed visible by then - no dependency on how quickly this
        // method goes on to actually acquire the gate.
        _autoReconnectSuppressed[groupId] = true;
        if (_connectCancellationSources.TryGetValue(groupId, out var inFlightCts))
        {
            inFlightCts.Cancel();
        }

        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            CancelPendingReconnect(groupId);

            // A manual disconnect sticks across an app restart too, not just
            // for the rest of this run - otherwise the group would just
            // reconnect the next time the app starts, which is exactly what
            // AutoConnect/PreferredLeaderSlotId being set means. Posted to
            // the UI thread for the same reasons as in SwitchLeaderAsync.
            if (group.Group.AutoConnect)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    group.Group.AutoConnect = false;
                    GroupPersistNeeded?.Invoke(group);
                });
            }

            if (!_leaderSlotByGroup.TryRemove(groupId, out var leaderSlotId))
            {
                SetConnectionState(group, ConnectionState.Disconnected);
                Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(null, null));
                return;
            }

            var leaderSlot = FindSlot(group, leaderSlotId);

            if (_sessions.TryRemove(leaderSlotId, out var session))
            {
                await TryCloseSocketAsync(group, leaderSlot, session);
            }

            if (leaderSlot is not null)
            {
                _messageHistoryService.HandleDisconnected(group, leaderSlot, "disconnected by user");
            }

            SetConnectionState(group, ConnectionState.Disconnected);
            Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(null, null));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot)
    {
        // Serialized against SwitchLeaderAsync/DisconnectGroupAsync via the
        // same per-group gate, so an externally-triggered catch-up (e.g.
        // "Add slot" on an already-connected group) can never open a second
        // connection while a SwitchLeaderAsync sibling sweep is already
        // using this group's one-at-a-time connection slot - it simply waits
        // its turn instead, preserving the "slots never connect concurrently"
        // invariant. The sweep itself calls CatchUpSyncCoreAsync directly
        // (it already holds this same gate) rather than back through here,
        // since SemaphoreSlim isn't reentrant.
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await CatchUpSyncCoreAsync(group, slot);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The actual catch-up logic, shared by the public <see cref="CatchUpSyncAsync"/>
    /// (gated there) and <see cref="SwitchLeaderAsync"/>'s own sibling sweep
    /// (already holding the group's gate, so it calls straight in here to
    /// avoid re-entering a non-reentrant <see cref="SemaphoreSlim"/>).
    /// </summary>
    private async Task CatchUpSyncCoreAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;

        // The slot may have been removed from the group after this catch-up
        // was queued up - e.g. it was still waiting its turn in a sibling
        // sweep's list, or an external CatchUpSyncAsync call for it was
        // queued behind the group's gate - and there is nothing left worth
        // syncing for a slot that no longer exists in the configuration by
        // the time its turn actually comes up.
        if (!group.Group.Slots.Any(s => s.Id == slot.Id))
        {
            return;
        }

        // A manual Disconnect means the user doesn't want any connection for
        // this group right now - including further catch-up dips for
        // whichever of a batch's new slots hadn't started yet. DisconnectGroupAsync
        // only cancels the ONE catch-up that happens to be in flight at the
        // exact moment Disconnect is clicked (via _connectCancellationSources);
        // MainWindowViewModel.CatchUpNewSlotsSequentiallyAsync just awaits
        // this method once per new slot in a plain loop and has no way to
        // learn a disconnect happened in between, so without this check
        // every remaining slot in the batch would still open its own brief
        // connection regardless. Cleared again by the next explicit
        // SwitchLeaderAsync - same lifecycle as auto-reconnect suppression,
        // which this flag doubles as.
        if (_autoReconnectSuppressed.ContainsKey(groupId))
        {
            return;
        }

        if (_leaderSlotByGroup.TryGetValue(groupId, out var leaderId))
        {
            if (leaderId == slot.Id)
            {
                return; // already the leader - already fully live, nothing to catch up.
            }

            // A leader is already live for this group. Its per-sibling hint
            // subscriptions (see ConnectSlotSessionAsync) only ever run once,
            // at the moment the leader itself connected - a slot that didn't
            // exist yet at that point (this call's whole reason for
            // existing: "Add slot..." on an already-connected group) would
            // otherwise stay invisible to the live leader session for as
            // long as it stays non-leader, same bug as the one fixed for
            // already-configured siblings, just for this narrower timing
            // window. Subscribe the already-open leader session to this
            // slot's own hint key right now too, over that same connection -
            // no reconnect needed.
            TrackHintsForSiblingOnLeader(group, leaderId, slot);
        }

        if (_sessions.ContainsKey(slot.Id))
        {
            return; // a connection for this slot is already in flight/open.
        }

        using var connectCts = new CancellationTokenSource();
        _connectCancellationSources[groupId] = connectCts;
        ArchipelagoSession? session;
        try
        {
            session = await ConnectSlotSessionAsync(group, slot, isLeaderSession: false, connectCts.Token);
        }
        finally
        {
            _connectCancellationSources.TryRemove(groupId, out _);
        }

        if (session is null)
        {
            return;
        }

        // Give the backlog burst (items delivered just after login, and the
        // first TrackHints callback) a moment to fully land before tearing
        // this temporary session back down.
        await Task.Delay(ItemBacklogGracePeriod);

        if (_sessions.TryRemove(slot.Id, out var stillTracked) && stillTracked == session)
        {
            await TryCloseSocketAsync(group, slot, session);
        }
    }

    public async Task InitializeGroupAsync(GroupViewModel group)
    {
        // A group that isn't set to auto-connect is meant to be fully
        // offline until the user explicitly reconnects it - that means
        // zero network activity at startup, not even a brief per-slot
        // catch-up dip. (Previously this ran a catch-up pass for every
        // configured slot regardless of AutoConnect, which looked like
        // "phantom" activity - live-looking events - for a server the user
        // had deliberately disconnected.) If/when the user does reconnect,
        // SwitchLeaderAsync itself catches up every other configured slot
        // as part of that connect - see its doc comment.
        if (!group.Group.AutoConnect || group.Group.Slots.Count == 0)
        {
            return;
        }

        var preferredId = group.Group.PreferredLeaderSlotId;
        var startupLeader = (preferredId is not null ? FindSlot(group, preferredId.Value) : null)
                             ?? group.Group.Slots[0];

        await SwitchLeaderAsync(group, startupLeader);
    }

    private void RaiseSlotInitialSyncCompleted(GroupViewModel group, SlotProfile slot) =>
        Dispatcher.UIThread.Post(() => SlotInitialSyncCompleted?.Invoke(group, slot));

    private void RaiseSlotSyncBatchStarting(int slotCount) =>
        Dispatcher.UIThread.Post(() => SlotSyncBatchStarting?.Invoke(slotCount));

    public async Task<IReadOnlyList<Archipelago.MultiClient.Net.Helpers.PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group)
    {
        var groupId = group.Group.Id;

        // Prefer an already-open session (the leader, or a catch-up dip
        // already in flight) over opening a redundant extra connection just
        // to read the roster.
        if (_leaderSlotByGroup.TryGetValue(groupId, out var leaderId) && _sessions.TryGetValue(leaderId, out var leaderSession))
        {
            return FilterToRealPlayers(leaderSession.Players.AllPlayers);
        }

        foreach (var slot in group.Group.Slots)
        {
            if (_sessions.TryGetValue(slot.Id, out var existingSession))
            {
                return FilterToRealPlayers(existingSession.Players.AllPlayers);
            }
        }

        // Nothing connected right now - open a brief temporary session using
        // whichever slot is already configured, purely to read the room's
        // current player list, then close it again immediately. Has the same
        // side effects as a catch-up sync for that one slot (harmless/
        // desirable on its own); it just doesn't wait out the full backlog
        // grace period before tearing down again.
        var probeSlot = group.Group.Slots.FirstOrDefault();
        if (probeSlot is null)
        {
            return Array.Empty<Archipelago.MultiClient.Net.Helpers.PlayerInfo>();
        }

        var session = await ConnectSlotSessionAsync(group, probeSlot, isLeaderSession: false);
        if (session is null)
        {
            return Array.Empty<Archipelago.MultiClient.Net.Helpers.PlayerInfo>();
        }

        var players = FilterToRealPlayers(session.Players.AllPlayers);

        if (_sessions.TryRemove(probeSlot.Id, out var stillTracked) && stillTracked == session)
        {
            await TryCloseSocketAsync(group, probeSlot, session);
        }

        return players;
    }

    /// <summary>
    /// Drops entries from a room's player list that can't actually be
    /// configured as a slot here: slot 0, reserved by the Archipelago
    /// protocol for the server process itself rather than any real player
    /// (commonly surfaced as a player literally named "Server"), and
    /// item-link groups (<see cref="Archipelago.MultiClient.Net.Helpers.PlayerInfo.IsGroup"/>) -
    /// virtual meta-slots representing several real slots' linked items,
    /// which can't be logged into on their own. Without this, both used to
    /// show up as pickable "players" in the "Add slot" dialog, and once
    /// added that way, as a bogus "Server"/group entry in the account
    /// dropdown and slot filters too.
    /// </summary>
    private static IReadOnlyList<Archipelago.MultiClient.Net.Helpers.PlayerInfo> FilterToRealPlayers(
        IEnumerable<Archipelago.MultiClient.Net.Helpers.PlayerInfo> players) =>
        players.Where(p => p.Slot != 0 && !p.IsGroup).ToList();

    public Task SendMessageAsync(GroupViewModel group, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            _leaderSlotByGroup.TryGetValue(group.Group.Id, out var leaderId) &&
            _sessions.TryGetValue(leaderId, out var session))
        {
            session.Say(text);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens and logs in a session for <paramref name="slot"/> and wires up
    /// its event handlers. Used for both leader connections (which stay open
    /// and get the chat/log broadcast subscription) and catch-up dips
    /// (<paramref name="isLeaderSession"/> = false, torn down again by the
    /// caller shortly after this returns). Returns null (having already
    /// reported the failure) if the connection or login fails.
    /// </summary>
    private async Task<ArchipelagoSession?> ConnectSlotSessionAsync(GroupViewModel group, SlotProfile slot, bool isLeaderSession, CancellationToken cancellationToken = default)
    {
        // Up to MaxTransientConnectRetries extra attempts, each with a
        // brand-new session, if the connect/login throws something that
        // looks like a transient hiccup rather than a real rejection - see
        // IsTransientConnectFailure. This is what covers "Connection failed
        // for X: A task was canceled" when switching leader shortly after
        // another slot on the same server just reconnected: the exact root
        // cause was never fully pinned down, but a canceled/timed-out
        // handshake is safe to just retry rather than surfacing a scary
        // error for what's usually a one-off blip. A genuine login
        // rejection (bad password etc.) never throws - it comes back as a
        // LoginFailure result instead, handled below and never retried.
        //
        // cancellationToken (from DisconnectGroupAsync, via the per-group
        // entry in _connectCancellationSources) lets a manual disconnect cut
        // this short. The underlying library's ConnectAsync()/LoginAsync()
        // accept no token of their own, so an already-in-flight call can't
        // be aborted mid-flight - cancellation is instead checked at every
        // natural checkpoint (top of each attempt, right after each of
        // those two calls returns, and during the between-retries delay),
        // which is enough to stop retrying and unwind promptly the moment
        // the current attempt concludes one way or another.
        for (var attempt = 1; attempt <= 1 + MaxTransientConnectRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            ArchipelagoSession session;
            try
            {
                session = ArchipelagoSessionFactory.CreateSession(group.Group.Host, group.Group.Port);
            }
            catch (Exception ex)
            {
                if (isLeaderSession)
                {
                    SetConnectionState(group, ConnectionState.Error);
                }

                _messageHistoryService.HandleError(group, $"Could not create session for {slot.SlotName}: {ex.Message}");
                return null;
            }

            // Set to true once the backlog grace period has elapsed after a
            // successful leader login; read by OnItemReceived so the backlog
            // delivered while logging in is shown as "received since last
            // connection", while items arriving later, truly live, are not
            // logged again there - the "X sent Y to Z" chat-log line already
            // announces those. Captured by reference, so the handler always sees
            // the up-to-date value. Irrelevant for catch-up sessions (isLeaderSession
            // false), which always treat everything as backlog - see OnItemReceived.
            // Declared fresh inside the loop body each attempt, so a retry's
            // closures capture that attempt's own flag, not a stale one from
            // a previous, abandoned session.
            var hasAnnouncedConnection = false;

            session.Socket.SocketClosed += reason => OnSocketClosed(group, slot, reason);
            session.Socket.ErrorReceived += (ex, message) =>
            {
                if (IsExpectedSendQueueCompletionError(ex))
                {
                    // Our own CompleteSendQueue() workaround (see
                    // TryCloseSocketAsync) deliberately provokes exactly this
                    // exception to unstick a catch-up session's SendLoop -
                    // it's expected, harmless, and means nothing to a player
                    // reading the event log ("what does 'collection argument'
                    // mean?"), so it's swallowed here instead of surfacing as
                    // a user-visible [SlotName] error.
                    return;
                }

                _messageHistoryService.HandleError(group, $"[{slot.SlotName}] {message}");
            };

            if (isLeaderSession)
            {
                // Only the leader's session stays open long enough for this to
                // matter - see OnLeaderMessageReceived for how non-leader slots
                // stay current passively from this same subscription.
                session.MessageLog.OnMessageReceived += logMessage => OnLeaderMessageReceived(group, session, slot, logMessage);
            }

            // Clear this slot's portion of the received-items panel before
            // subscribing. The server re-delivers the full item history on every
            // connect (via PerformResynchronization), so this slot's entries are
            // rebuilt from scratch each time; other slots' entries in the same
            // merged list are untouched. Dispatching before the subscription
            // ensures the clear runs on the UI thread before any item-added posts
            // arrive for this slot.
            _messageHistoryService.ClearReceivedItemsForSlot(group, slot.Id);

            session.Items.ItemReceived += helper => OnItemReceived(group, slot, session, helper, isLeaderSession, hasAnnouncedConnection);

            try
            {
                await session.ConnectAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    // Disconnected while the connect itself was in flight -
                    // the library gives no way to abort that call early, but
                    // this is the first chance to notice afterward; skip
                    // logging in and tear the attempt back down instead of
                    // finishing a handshake nobody wants anymore.
                    await TryCloseSocketAsync(group, slot, session);
                    return null;
                }

                // Most rooms share one password for every slot (group.Group.Password);
                // slot.Password is only set when a slot was added with an explicit
                // override for a custom-hosted room that uses a different
                // password per slot (see Views/ConnectionEditorWindow "Add slot").
                var effectivePassword = !string.IsNullOrEmpty(slot.Password) ? slot.Password : group.Group.Password;

                var loginResult = await session.LoginAsync(
                    string.Empty, // generic tracker client: no specific game implementation
                    slot.SlotName,
                    ItemsHandlingFlags.AllItems,
                    ArchipelagoProtocolVersion,
                    tags: new[] { "Tracker", "AP", "Poly" },
                    password: string.IsNullOrEmpty(effectivePassword) ? null : effectivePassword);

                if (cancellationToken.IsCancellationRequested)
                {
                    await TryCloseSocketAsync(group, slot, session);
                    return null;
                }

                if (loginResult is not LoginSuccessful)
                {
                    var failure = (LoginFailure)loginResult;
                    var errorText = string.Join("; ", failure.Errors);
                    if (isLeaderSession)
                    {
                        SetConnectionState(group, ConnectionState.Error);
                    }

                    _messageHistoryService.HandleError(group, $"Login failed for {slot.SlotName}: {errorText}");
                    await TryCloseSocketAsync(group, slot, session);
                    return null; // a real rejection, not a transient hiccup - never retried.
                }
            }
            catch (Exception ex) when (IsTransientConnectFailure(ex) && attempt <= MaxTransientConnectRetries)
            {
                // This attempt's session is now in an indeterminate state -
                // discard it and retry with a completely fresh one rather
                // than reusing it.
                await TryCloseSocketAsync(group, slot, session);
                _messageHistoryService.HandleError(
                    group,
                    $"Connecting {slot.SlotName} hit a transient error ({ex.GetType().Name}), retrying ({attempt}/{MaxTransientConnectRetries})...");

                try
                {
                    await Task.Delay(TransientConnectRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled during the backoff pause itself - stop
                    // retrying immediately rather than waiting it out.
                    return null;
                }

                continue;
            }
            catch (Exception ex)
            {
                if (isLeaderSession)
                {
                    SetConnectionState(group, ConnectionState.Error);
                }

                _messageHistoryService.HandleError(group, $"Connection failed for {slot.SlotName}: {ex.Message}");
                await TryCloseSocketAsync(group, slot, session);
                return null;
            }

            _sessions[slot.Id] = session;

            // Fires immediately with this slot's own currently unlocked hints,
            // then again on every later change to them.
            //
            // Unlike chat/item-send lines, a hint's server-side notification is
            // NOT broadcast to the whole room - the AP server only pushes the
            // "Hint: ..." text (and the underlying hints_{team}_{slot} DataStorage
            // update this subscribes to) to the finder's and the receiver's own
            // clients (see MultiServer.py's notify_hints/`concerns`). So this
            // single subscription only ever reports hints where *this* slot is
            // the finder or receiver. Fine for a brief catch-up session (it only
            // cares about itself), but for the leader - the only session that
            // stays open live - it would otherwise silently miss any hint that
            // concerns a different configured sibling slot instead (e.g. a
            // sibling running !hint_location on its own location) for as long
            // as that sibling stays non-leader. See the per-sibling subscriptions
            // added below for the leader case.
            session.Hints.TrackHints(
                hints => OnHintsUpdated(group, session, hints),
                retrieveCurrentlyUnlockedHints: true);

            if (isLeaderSession)
            {
                // Track every other configured slot's own hint list too, over
                // this same already-open connection - no extra login needed,
                // just one more DataStorage subscription per sibling. This is
                // what actually makes hint coverage "the whole group" while the
                // leader is live, the way chat/item-send lines already are for
                // free via the room-wide MessageLog broadcast.
                var roster = BuildSlotRoster(session, group.Group);
                foreach (var (numericSlotId, siblingSlot) in roster)
                {
                    if (siblingSlot.Id == slot.Id)
                    {
                        continue; // this leader slot is already covered above
                    }

                    session.Hints.TrackHints(
                        hints => OnHintsUpdated(group, session, hints),
                        retrieveCurrentlyUnlockedHints: true,
                        slot: numericSlotId);
                }

                _messageHistoryService.HandleConnected(group, slot);
            }

            _ = Task.Delay(ItemBacklogGracePeriod)
                    .ContinueWith(_ => hasAnnouncedConnection = true, TaskScheduler.Default);

            return session;
        }

        // Unreachable: every loop iteration either returns or, on its very
        // last allowed attempt, falls into the plain catch block above
        // (whose exception filter requires attempt <= MaxTransientConnectRetries)
        // and returns null instead of looping again.
        return null;
    }

    /// <summary>
    /// Exceptions that look like a transient network/timing hiccup - worth
    /// silently retrying with a fresh session - rather than a real
    /// connection rejection. <see cref="TaskCanceledException"/> in
    /// particular is what the underlying library throws for the
    /// "A task was canceled" failure seen when switching leader shortly
    /// after another slot on the same server just reconnected.
    /// </summary>
    private static bool IsTransientConnectFailure(Exception ex) =>
        ex is TaskCanceledException or OperationCanceledException or TimeoutException;

    /// <summary>
    /// Closes <paramref name="session"/>'s socket and actually waits for that
    /// to finish before returning - previously this fired
    /// <c>DisconnectAsync()</c> and returned immediately without awaiting the
    /// Task it gave back, so a slow or failing close handshake was
    /// completely invisible and, worse, never actually guaranteed to happen
    /// before the caller moved on. That mattered a lot right here: every
    /// slot in <see cref="CatchUpSyncAsync"/>'s startup loop tears down its
    /// session this way, one slot after another - fire-and-forget meant a
    /// slow disconnect from slot N could still be in flight when slot N+2's,
    /// N+3's etc. sessions were created, each with their own full
    /// <c>ArchipelagoSession</c> (item/location name tables and all) that
    /// then had nothing left to make it exit - the underlying library's
    /// background receive loop only stops once the socket's own state
    /// leaves "Open", which a disconnect that never got to run/finish would
    /// never cause. Observed in practice on a ~40-slot room: every one of
    /// those sessions was still fully resident in memory (a multi-hundred-MB
    /// to multi-GB working set) long after the startup sync had finished.
    /// Awaiting here bounds this to at most one slot's teardown in flight at
    /// a time, matching the "slots never connect concurrently" invariant
    /// this class already keeps for connects.
    /// </summary>
    private async Task TryCloseSocketAsync(GroupViewModel group, SlotProfile? slot, ArchipelagoSession session)
    {
        try
        {
            await session.Socket.DisconnectAsync();
        }
        catch
        {
            // Best-effort cleanup; nothing more to do.
        }

        // Workaround for two real, independent bugs in Archipelago.MultiClient.Net,
        // both verified against its current source (still present as of
        // 6.7.1, the latest release) and both confirmed with a live GC-root
        // trace: every ArchipelagoSession ever created during a ~40-slot
        // startup catch-up sync was still fully alive and reachable
        // afterward - each with its own multi-ten-MB per-session
        // item/location name cache - adding up to a multi-hundred-MB to
        // multi-GB working set. DisconnectAsync() above only sends a
        // graceful close frame; it touches neither of the two fire-and-forget
        // background loops (Task.Run(PollingLoop), Task.Run(SendLoop)) that
        // ConnectAsync starts and that this session's whole object graph is
        // reachable through:
        //
        // 1. PollingLoop keeps calling the underlying ClientWebSocket's
        //    ReceiveAsync() with no cancellation token of its own. Calling
        //    CloseAsync while a ReceiveAsync from a different call site is
        //    still pending on the same ClientWebSocket is a known .NET
        //    WebSocket foot-gun - the pending receive is left dangling
        //    instead of being unblocked. Fixed below by reaching the
        //    internal ClientWebSocket field directly (see
        //    FindClientWebSocket) and calling Abort() on it, which
        //    immediately faults any pending ReceiveAsync/SendAsync and flips
        //    the socket's state away from Open.
        // 2. SendLoop calls `sendQueue.Take()` (a *blocking*, not async,
        //    call) to wait for the next outgoing packet. Nothing ever calls
        //    `sendQueue.CompleteAdding()` on disconnect, so if SendLoop is
        //    sitting in that Take() with nothing queued - the common case
        //    for a catch-up session, which never sends anything after
        //    logging in - it stays blocked forever; the `while (Socket.State
        //    == WebSocketState.Open)` loop condition around it is only
        //    re-checked *after* Take() returns, so aborting the socket alone
        //    (fix 1 above) doesn't reach it. Confirmed to be the second half
        //    of the leak: fix 1 alone left every session's ClientWebSocket
        //    correctly Disposed, yet every session still rooted - a live
        //    GC-root trace pointed straight at a still-running SendLoop
        //    stack frame. Fixed below the same way, via CompleteSendQueue.
        //
        // Both reach past IArchipelagoSocketHelper's public contract into
        // internal implementation details, so both are inherently fragile -
        // a future Archipelago.MultiClient.Net release could rename or
        // restructure either field, silently turning this back into a
        // no-op - which is why each is a single best-effort attempt that
        // never throws past this method rather than something callers
        // depend on.
        try
        {
            if (FindClientWebSocket(session.Socket) is { } rawSocket)
            {
                rawSocket.Abort();
            }
        }
        catch (Exception ex)
        {
            // Best-effort; if the internal shape ever changes, this simply
            // stops helping instead of breaking anything - but that would
            // silently reopen the memory leak this exists to fix, so it's
            // logged visibly (as a normal Error event, same as any other
            // connection problem) rather than swallowed without a trace.
            _messageHistoryService.HandleError(
                group, $"[{slot?.SlotName ?? "unknown slot"}] internal cleanup workaround (socket abort) failed: {ex.Message}");
        }

        try
        {
            CompleteSendQueue(session.Socket);
        }
        catch (Exception ex)
        {
            // See above - same reasoning, other half of the workaround.
            _messageHistoryService.HandleError(
                group, $"[{slot?.SlotName ?? "unknown slot"}] internal cleanup workaround (send queue) failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reaches through <see cref="IArchipelagoSocketHelper"/> - which exposes
    /// no way to forcibly abort the connection - to the internal
    /// <c>ClientWebSocket</c> field that the library's concrete socket
    /// helper actually reads from, so <see cref="TryCloseSocketAsync"/> can
    /// abort it directly. See that method's doc comment for why this is
    /// needed at all. Walks up the type hierarchy since the field
    /// (<c>internal T Socket;</c>) is declared on the open generic base
    /// class <c>BaseArchipelagoSocketHelper&lt;T&gt;</c>, not on the
    /// concrete <c>ArchipelagoSocketHelper</c> type reflection starts from -
    /// <see cref="Type.GetField(string, BindingFlags)"/> alone only finds
    /// members declared directly on the type passed in, not inherited
    /// non-public ones.
    /// </summary>
    private static WebSocket? FindClientWebSocket(IArchipelagoSocketHelper socketHelper)
    {
        for (var type = socketHelper.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("Socket", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field is not null)
            {
                return field.GetValue(socketHelper) as WebSocket;
            }
        }

        return null;
    }

    /// <summary>
    /// Reaches through <see cref="IArchipelagoSocketHelper"/> to the internal
    /// <c>sendQueue</c> (a <c>BlockingCollection&lt;...&gt;</c>) that
    /// <c>BaseArchipelagoSocketHelper&lt;T&gt;.SendLoop</c> blocks on via a
    /// plain, non-cancellable <c>Take()</c>, and calls its non-generic
    /// <c>CompleteAdding()</c> - which makes a <c>Take()</c> blocked on an
    /// empty, now-completed queue throw instead of waiting forever - so
    /// <see cref="TryCloseSocketAsync"/> can unstick a SendLoop that has
    /// nothing left to send (the normal case for a catch-up session, which
    /// never sends anything after logging in). See that method's doc
    /// comment for why this is needed at all. Reflects on the queue's own
    /// value rather than casting to a known
    /// <c>BlockingCollection&lt;T&gt;</c>, since the element type is itself
    /// an internal tuple type not worth reproducing here just to satisfy the
    /// compiler for a single no-argument method call.
    /// </summary>
    private static void CompleteSendQueue(IArchipelagoSocketHelper socketHelper)
    {
        for (var type = socketHelper.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField("sendQueue", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field is null)
            {
                continue;
            }

            var queue = field.GetValue(socketHelper);
            queue?.GetType().GetMethod("CompleteAdding", BindingFlags.Public | BindingFlags.Instance)?.Invoke(queue, null);
            return;
        }
    }

    /// <summary>
    /// True for the specific <see cref="InvalidOperationException"/> that
    /// <see cref="CompleteSendQueue"/> deliberately provokes: the library's
    /// own <c>SendLoop</c> is sitting in a blocking <c>sendQueue.Take()</c>
    /// with nothing left to send (the normal case for a catch-up session,
    /// which never sends anything after logging in), and calling
    /// <c>CompleteAdding()</c> on that queue - our only way to unstick it,
    /// see <see cref="TryCloseSocketAsync"/> - makes that blocked call throw
    /// this exact exception instead of waiting forever. It's the expected,
    /// intended *result* of our own workaround, not a real failure, so it's
    /// filtered out here rather than surfaced as a "[SlotName] ..." error a
    /// player would have no way to make sense of. Matched on both the
    /// exception type and its message (BlockingCollection's own wording,
    /// not ours) so a genuine, unrelated InvalidOperationException from the
    /// socket still gets reported normally.
    /// </summary>
    private static bool IsExpectedSendQueueCompletionError(Exception ex) =>
        ex is InvalidOperationException &&
        ex.Message.Contains("marked as complete with regards to additions", StringComparison.OrdinalIgnoreCase);

    private void OnSocketClosed(GroupViewModel group, SlotProfile slot, string reason)
    {
        var wasConnected = _sessions.TryRemove(slot.Id, out _);
        if (!wasConnected)
        {
            // Either the handshake was still pending when the socket closed,
            // or this is a deliberate close (switch/disconnect/catch-up
            // teardown) that already removed the slot from _sessions and
            // handled its own logging/state right away - see SwitchLeaderAsync,
            // DisconnectGroupAsync and CatchUpSyncAsync. Nothing left to do.
            return;
        }

        var groupId = group.Group.Id;
        var isStillTheLeader = _leaderSlotByGroup.TryGetValue(groupId, out var leaderId) && leaderId == slot.Id;

        if (!isStillTheLeader)
        {
            // A catch-up session that happened to close itself before the
            // caller's own teardown ran, or a switch/disconnect whose
            // bookkeeping already moved on. Either way, already handled.
            return;
        }

        _messageHistoryService.HandleDisconnected(group, slot, reason);
        _leaderSlotByGroup.TryRemove(groupId, out _);
        Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(null, null));

        if (!_autoReconnectSuppressed.ContainsKey(groupId) && group.Group.AutoConnect)
        {
            SetConnectionState(group, ConnectionState.Reconnecting);
            _ = ScheduleReconnectAsync(group, slot);
        }
        else
        {
            SetConnectionState(group, ConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// Tries to bring the leader back as the same slot that just dropped
    /// unexpectedly (not necessarily <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/> -
    /// that field only decides who connects at startup; an unexpected drop
    /// reconnects whoever was actually leader when it happened).
    /// </summary>
    private async Task ScheduleReconnectAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;
        var cts = new CancellationTokenSource();
        _reconnectTokens[groupId] = cts;

        try
        {
            var attempt = 0;
            while (!cts.IsCancellationRequested && group.Group.AutoConnect && !_sessions.ContainsKey(slot.Id))
            {
                var delaySeconds = ReconnectDelaysSeconds[Math.Min(attempt, ReconnectDelaysSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token);

                if (cts.IsCancellationRequested)
                {
                    break;
                }

                SetConnectionState(group, ConnectionState.Reconnecting);
                await SwitchLeaderAsync(group, slot);

                attempt++;
            }
        }
        catch (TaskCanceledException)
        {
            // Reconnect loop was cancelled by a manual switch/disconnect; nothing to do.
        }
        finally
        {
            _reconnectTokens.TryRemove(groupId, out _);
        }
    }

    private void CancelPendingReconnect(Guid groupId)
    {
        if (_reconnectTokens.TryRemove(groupId, out var cts))
        {
            cts.Cancel();
        }
    }

    private static SlotProfile? FindSlot(GroupViewModel group, Guid slotId) =>
        group.Group.Slots.FirstOrDefault(s => s.Id == slotId);

    /// <summary>
    /// Maps every configured slot in <paramref name="serverGroup"/> to its
    /// numeric Archipelago slot id in the room, by matching
    /// <see cref="SlotProfile.SlotName"/> against the room's player roster.
    /// Rebuilt on every call (one cheap linear scan over a small list)
    /// rather than cached, so it always reflects the current <c>Slots</c>
    /// collection even if a slot was added/removed after the leader logged
    /// in. Lets a slot that has never itself been connected still be
    /// recognized in chat/item/hint broadcasts observed via another slot's
    /// session - see <see cref="OnLeaderMessageReceived"/> and
    /// <see cref="OnHintsUpdated"/>.
    /// </summary>
    private static Dictionary<int, SlotProfile> BuildSlotRoster(ArchipelagoSession session, ServerConnectionGroup serverGroup)
    {
        var roster = new Dictionary<int, SlotProfile>();
        foreach (var slot in serverGroup.Slots)
        {
            var match = session.Players.AllPlayers.FirstOrDefault(p =>
                string.Equals(p.Name, slot.SlotName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                roster[match.Slot] = slot;
            }
        }

        return roster;
    }

    /// <summary>
    /// Adds one more <c>TrackHints</c> subscription to <paramref name="leaderId"/>'s
    /// already-open session, for <paramref name="newSlot"/>'s own numeric
    /// slot id - see the call site in <see cref="CatchUpSyncAsync"/> for why
    /// this exists (a slot added while a leader is already connected isn't
    /// covered by the sibling loop in <see cref="ConnectSlotSessionAsync"/>,
    /// which only runs once, at the moment the leader itself connects). A
    /// no-op if the leader's session isn't tracked for some reason, or if
    /// <paramref name="newSlot"/>'s configured name doesn't (yet) match a
    /// real player in the room roster - nothing to subscribe to in that
    /// case, same as any other unresolved sibling name.
    /// </summary>
    private void TrackHintsForSiblingOnLeader(GroupViewModel group, Guid leaderId, SlotProfile newSlot)
    {
        if (!_sessions.TryGetValue(leaderId, out var leaderSession))
        {
            return;
        }

        var roster = BuildSlotRoster(leaderSession, group.Group);
        foreach (var (numericSlotId, matchedSlot) in roster)
        {
            if (matchedSlot.Id == newSlot.Id)
            {
                leaderSession.Hints.TrackHints(
                    hints => OnHintsUpdated(group, leaderSession, hints),
                    retrieveCurrentlyUnlockedHints: true,
                    slot: numericSlotId);
                return;
            }
        }
    }

    private static HashSet<int> SiblingIdsExcludingOwn(Dictionary<int, SlotProfile> roster, int ownNumericSlotId) =>
        new(roster.Keys.Where(id => id != ownNumericSlotId));

    /// <summary>
    /// Which single configured slot (if any) a log/chat message is "about",
    /// for tagging the merged event log entry's <see cref="EventEntry.SlotId"/>
    /// so the slot filter dropdown can narrow to it. An item-send message
    /// resolves to its receiving slot; anything else resolves to the first
    /// configured slot mentioned in the message's parts, if any. Null (shown
    /// regardless of the slot filter) for messages that don't mention any
    /// configured slot at all - room-wide banter between unrelated players.
    /// </summary>
    private static Guid? ResolvePrimarySlotId(LogMessage logMessage, Dictionary<int, SlotProfile> roster)
    {
        if (logMessage is ItemSendLogMessage send && roster.TryGetValue(send.Receiver.Slot, out var receivingSlot))
        {
            return receivingSlot.Id;
        }

        foreach (var part in logMessage.Parts)
        {
            if (part is PlayerMessagePart playerPart && roster.TryGetValue(playerPart.SlotId, out var matched))
            {
                return matched.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Fires for every chat/log line the leader's session receives - which
    /// covers the whole room, not just the leader's own slot. Appends the
    /// line to the group's merged event log, and - the core mechanism behind
    /// Phase 6 - mirrors an item-send broadcast into a non-leader configured
    /// slot's own received-items panel, so that slot stays current without
    /// ever needing its own connection.
    /// </summary>
    private void OnLeaderMessageReceived(GroupViewModel group, ArchipelagoSession session, SlotProfile leaderSlot, LogMessage logMessage)
    {
        var roster = BuildSlotRoster(session, group.Group);
        var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);

        var segments = EventSegmentBuilder.BuildChatSegments(logMessage, siblingIds);
        var slotId = ResolvePrimarySlotId(logMessage, roster);
        var eventType = logMessage is ItemSendLogMessage ? EventType.ItemReceived : EventType.Chat;
        _messageHistoryService.HandleChatMessage(group, logMessage.ToString(), segments, slotId, eventType);

        // Confirmed against the installed Archipelago.MultiClient.Net 6.7.1 XML
        // docs: ItemSendLogMessage exposes the receiving side as `.Receiver`
        // (a PlayerInfo, with a `.Slot` int), not `.Receiving`.
        if (logMessage is ItemSendLogMessage itemSend &&
            itemSend.Receiver.Slot != session.ConnectionInfo.Slot &&
            roster.TryGetValue(itemSend.Receiver.Slot, out var targetSlot))
        {
            var senderKind = EventSegmentBuilder.ClassifyPlayerSlot(itemSend.Item.Player, session.ConnectionInfo.Slot, siblingIds);
            var senderName = session.Players.GetPlayerAlias(itemSend.Item.Player) ?? string.Empty;
            _messageHistoryService.HandleObservedItemForSlot(
                group, targetSlot,
                itemSend.Item.ItemDisplayName, itemSend.Item.LocationDisplayName, itemSend.Item.Flags,
                senderName, senderKind);
        }
    }

    private void OnItemReceived(GroupViewModel group, SlotProfile slot, ArchipelagoSession session, ReceivedItemsHelper helper, bool isLeaderSession, bool hasAnnouncedConnection)
    {
        // A catch-up session always treats everything as backlog - its whole
        // point is a one-shot "give me your full current state" resync, not
        // an ongoing live connection.
        var isLive = isLeaderSession && hasAnnouncedConnection;

        if (isLive)
        {
            // Already connected - the chat-log "X sent Y to Z" line handles
            // announcing this one; just keep the persisted index moving.
            _messageHistoryService.AdvanceItemSyncState(slot, helper.AllItemsReceived);
        }
        else
        {
            _messageHistoryService.HandleItemsReceivedSinceLastConnection(group, slot, helper.AllItemsReceived);
        }

        var senderName = string.Empty;
        var senderKind = EventTextSegmentKind.OtherSlotName;
        if (helper.AllItemsReceived.Count > 0)
        {
            var latest = helper.AllItemsReceived[helper.AllItemsReceived.Count - 1];
            senderName = session.Players.GetPlayerAlias(latest.Player) ?? string.Empty;
            var roster = BuildSlotRoster(session, group.Group);
            var siblingIds = SiblingIdsExcludingOwn(roster, session.ConnectionInfo.Slot);
            senderKind = EventSegmentBuilder.ClassifyPlayerSlot(latest.Player, session.ConnectionInfo.Slot, siblingIds);
        }

        _messageHistoryService.TrackReceivedItem(group, slot, helper.AllItemsReceived, senderName, senderKind);

        // Drain the queue as documented by the library; the calls above already
        // read everything they need from AllItemsReceived.
        while (helper.Any())
        {
            helper.DequeueItem();
        }
    }

    /// <summary>
    /// Fires whenever one tracked slot's full current hint list is available -
    /// once per <c>TrackHints</c> subscription registered above, each scoped to
    /// a single slot (its own by default, or an explicit sibling slot for the
    /// leader's extra subscriptions). Not room-wide by itself: each call's
    /// <paramref name="hints"/> only ever contains hints where that one tracked
    /// slot is the finder or receiver. A hint where neither side resolves to
    /// one of this group's configured slots is skipped entirely; where one
    /// does, the hint is routed to that slot (preferring the receiving side if
    /// both happen to be configured slots of this same group).
    /// </summary>
    private void OnHintsUpdated(GroupViewModel group, ArchipelagoSession session, Hint[] hints)
    {
        var roster = BuildSlotRoster(session, group.Group);
        if (roster.Count == 0 || hints.Length == 0)
        {
            return;
        }

        var ownSlotNumeric = session.ConnectionInfo.Slot;
        var siblingIds = SiblingIdsExcludingOwn(roster, ownSlotNumeric);

        var snapshots = new List<HintSnapshot>(hints.Length);

        foreach (var hint in hints)
        {
            Guid? slotId = null;
            if (roster.TryGetValue(hint.ReceivingPlayer, out var receivingSlot))
            {
                slotId = receivingSlot.Id;
            }
            else if (roster.TryGetValue(hint.FindingPlayer, out var findingSlot))
            {
                slotId = findingSlot.Id;
            }

            if (slotId is null)
            {
                continue;
            }

            // Items belong to the receiving player's game; locations belong to the
            // finding player's game - the generic tracker login uses an empty game,
            // so the correct game must be looked up per hinted player to resolve names.
            var receivingGame = session.Players.GetPlayerInfo(hint.ReceivingPlayer)?.Game;
            var findingGame = session.Players.GetPlayerInfo(hint.FindingPlayer)?.Game;

            snapshots.Add(new HintSnapshot
            {
                Key = $"{hint.ReceivingPlayer}:{hint.FindingPlayer}:{hint.ItemId}:{hint.LocationId}",
                SlotId = slotId.Value,
                ReceivingPlayer = hint.ReceivingPlayer,
                FindingPlayer = hint.FindingPlayer,
                ReceivingPlayerName = session.Players.GetPlayerAlias(hint.ReceivingPlayer),
                FindingPlayerName = session.Players.GetPlayerAlias(hint.FindingPlayer),
                ItemName = session.Items.GetItemName(hint.ItemId, receivingGame) ?? $"Item #{hint.ItemId}",
                LocationName = session.Locations.GetLocationNameFromId(hint.LocationId, findingGame) ?? $"Location #{hint.LocationId}",
                Found = hint.Found,
                ItemFlags = hint.ItemFlags,
                ReceivingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.ReceivingPlayer, ownSlotNumeric, siblingIds),
                FindingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.FindingPlayer, ownSlotNumeric, siblingIds)
            });
        }

        if (snapshots.Count > 0)
        {
            _hintService.SyncHints(group, snapshots);
        }
    }

    private static void SetConnectionState(GroupViewModel group, ConnectionState state) =>
        Dispatcher.UIThread.Post(() => group.ConnectionState = state);
}
