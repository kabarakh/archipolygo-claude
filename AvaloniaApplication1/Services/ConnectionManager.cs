using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
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
/// Phase 6 rewrite of the original one-session-per-slot model - see
/// Umsetzungsplan.md, Phase 6, for the full design rationale. The detailed
/// why-rationale for this class's trickier corners (locking, retries, the
/// socket-cleanup workaround, password flow, ...) lives in Umsetzungsplan.md's
/// "ConnectionManager: Locking, Nebenläufigkeit und Workarounds im Detail"
/// section; comments below point at the matching subsection by name.
/// </summary>
public class ConnectionManager : IConnectionManager
{
    // Backoff schedule for leader auto-reconnect attempts; the last value repeats for further attempts.
    private static readonly int[] ReconnectDelaysSeconds = { 5, 10, 30 };

    // Spacing between the *start* of two consecutive groups' startup connect
    // attempts, so a restart with several AutoConnect servers doesn't burst
    // them all at once. See MainWindowViewModel.InitializeGroupsAsync.
    public static readonly TimeSpan StartupGroupSpacing = TimeSpan.FromSeconds(3);

    // How long to treat freshly received items as backlog rather than live
    // receipts after a leader login, and how long a catch-up session waits
    // after login before closing itself. See Umsetzungsplan.md, section
    // "Warum der Item-Backlog eine Grace-Period braucht".
    private static readonly TimeSpan DefaultItemBacklogGracePeriod = TimeSpan.FromSeconds(2);

    // The Archipelago network-protocol version this client implements (used
    // in the login handshake) - not this app's own version number.
    private static readonly Version ArchipelagoProtocolVersion = new(0, 6, 7);

    // How many times ConnectSlotSessionAsync retries a connect+login that
    // keeps failing with what looks like a transient hiccup (see
    // IsTransientConnectFailure) - 1 initial attempt plus this many retries.
    private const int MaxTransientConnectRetries = 2;

    // Pause between a transient-looking failed attempt and the next retry.
    private static readonly TimeSpan DefaultTransientConnectRetryDelay = TimeSpan.FromSeconds(2);

    // How long GetHintableItemsAsync waits for the server's DataPackagePacket
    // response before giving up and returning whatever's cached (or empty).
    private static readonly TimeSpan DefaultDataPackageRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;
    private readonly ISessionFactory _sessionFactory;

    // Optional - null wherever a test construction site doesn't care about
    // the Hint-picker's Item-mode DataPackage cache; null just means
    // GetHintableItemsAsync always fetches fresh instead of checking disk.
    private readonly IPersistenceService? _persistenceService;

    // Optional; falls back to NullDiagnosticLogger so every existing test
    // construction site keeps compiling without writing a real log.
    private readonly IDiagnosticLogger _diagnosticLogger;

    private readonly TimeSpan _dataPackageRequestTimeout;

    // Constructor-overridable so tests don't really wait several seconds per
    // call. See Umsetzungsplan.md, section "Item-Backlog-Grace-Period und
    // Retry-Delay (Testbarkeit)".
    private readonly TimeSpan _itemBacklogGracePeriod;
    private readonly TimeSpan _transientConnectRetryDelay;

    // Keyed by SlotProfile.Id; a session exists here for as long as that
    // slot has any active connection. See Umsetzungsplan.md, section
    // "_sessions: Lifecycle und maximale gleichzeitige Einträge".
    private readonly ConcurrentDictionary<Guid, IArchipelagoSession> _sessions = new();

    // Turns raw session events (chat/item/hint) into MessageHistoryService/
    // HintService calls, and owns the missing-locations cache for the Hint
    // picker - split out since it needs almost none of this class's
    // connection/leader state. See that class's own doc comment.
    private readonly SessionEventTranslator _sessionEvents;

    // Closes a session's socket and applies the memory-leak workaround -
    // split out for the same reason as _sessionEvents above.
    private readonly SocketCleanup _socketCleanup;

    // Keyed by SlotProfile.Id; that slot's most recent RoomInfoPacket. See
    // Umsetzungsplan.md, section "_roomInfoBySlot".
    private readonly ConcurrentDictionary<Guid, RoomInfoPacket> _roomInfoBySlot = new();

    // GroupId -> the SlotProfile.Id that currently holds the persistent
    // leader connection. Absent = the group has no leader right now.
    private readonly ConcurrentDictionary<Guid, Guid> _leaderSlotByGroup = new();

    // GroupId -> a lock serializing SwitchLeaderAsync/DisconnectGroupAsync/
    // CatchUpSyncAsync for that group, so they can never race each other.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _groupLocks = new();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reconnectTokens = new();

    // GroupId -> the CancellationTokenSource for whichever single connect
    // attempt is currently in flight for that group. See Umsetzungsplan.md,
    // section "_connectCancellationSources".
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _connectCancellationSources = new();

    // Set right before a deliberate DisconnectGroupAsync, cleared only by a
    // later explicit SwitchLeaderAsync. See Umsetzungsplan.md, section
    // "_autoReconnectSuppressed".
    private readonly ConcurrentDictionary<Guid, bool> _autoReconnectSuppressed = new();

    /// <inheritdoc/>
    public event Action<GroupViewModel>? GroupPersistNeeded;

    /// <inheritdoc/>
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <inheritdoc/>
    public event Action<int>? SlotSyncBatchStarting;

    /// <inheritdoc/>
    public Func<GroupViewModel, SlotProfile, bool, CancellationToken, Task<bool>>? PasswordRequested { get; set; }

    public ConnectionManager(
        IMessageHistoryService messageHistoryService,
        IHintService hintService,
        ISessionFactory sessionFactory,
        TimeSpan? itemBacklogGracePeriod = null,
        TimeSpan? transientConnectRetryDelay = null,
        IPersistenceService? persistenceService = null,
        TimeSpan? dataPackageRequestTimeout = null,
        IDiagnosticLogger? diagnosticLogger = null)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
        _sessionFactory = sessionFactory;
        _itemBacklogGracePeriod = itemBacklogGracePeriod ?? DefaultItemBacklogGracePeriod;
        _transientConnectRetryDelay = transientConnectRetryDelay ?? DefaultTransientConnectRetryDelay;
        _persistenceService = persistenceService;
        _dataPackageRequestTimeout = dataPackageRequestTimeout ?? DefaultDataPackageRequestTimeout;
        _diagnosticLogger = diagnosticLogger ?? NullDiagnosticLogger.Instance;
        _sessionEvents = new SessionEventTranslator(_messageHistoryService, _hintService);
        _socketCleanup = new SocketCleanup(_messageHistoryService, _diagnosticLogger);
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

            // A manual switch wins over any pending auto-reconnect and lifts
            // a previous manual disconnect's suppression.
            CancelPendingReconnect(groupId);
            _autoReconnectSuppressed.TryRemove(groupId, out _);

            SetConnectionState(group, ConnectionState.Connecting);

            // Announce just the leader attempt for now - see
            // Umsetzungsplan.md, section "SlotSyncBatchStarting: warum erst
            // 1, dann die Sibling-Anzahl".
            RaiseSlotSyncBatchStarting(1);

            // Connect the new leader first, only close the old one once that
            // succeeds. See Umsetzungsplan.md, section "Leader-Wechsel:
            // bewusste kurze Überlappung".
            using var connectCts = new CancellationTokenSource();
            _connectCancellationSources[groupId] = connectCts;
            IArchipelagoSession? newSession;
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
                // Already reported via HandleError/ConnectionState.Error
                // inside ConnectSlotSessionAsync. The previous leader (if
                // any) was never touched - restore the dropdown/leader
                // bookkeeping to match instead of pointing at a slot that
                // never connected.
                var stillLeaderId = _leaderSlotByGroup.TryGetValue(groupId, out var stillLeader) ? (Guid?)stillLeader : null;
                var stillLeaderSlot = stillLeaderId is null ? null : FindSlot(group, stillLeaderId.Value);
                Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(stillLeaderId, stillLeaderSlot));

                // Closes out the announced 1-slot batch even though it
                // failed, so the progress indicator isn't left stuck short.
                RaiseSlotInitialSyncCompleted(group, targetSlot);
                return;
            }

            var previousLeaderId = _leaderSlotByGroup.TryGetValue(groupId, out var prev) ? (Guid?)prev : null;
            _leaderSlotByGroup[groupId] = targetSlot.Id;
            Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(targetSlot.Id, targetSlot));
            SetConnectionState(group, ConnectionState.Connected);

            // A successful leader connect, by any means, is itself what
            // brings this group back at the next app start - remembered
            // here rather than a separate "Auto-connect" opt-in. Posted to
            // the UI thread like every group mutation below.
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
                    _diagnosticLogger.Info($"[{group.Group.Name}] Switched leader from {previousSlot.DisplayName} to {targetSlot.DisplayName}");
                }

                await _socketCleanup.CloseAsync(group, previousSlot, oldSession);
            }

            // The leader already got its own full backlog via login; report
            // it done right away rather than waiting on the sibling loop
            // below (feeds the startup "Catching up slots: N/M" banner).
            RaiseSlotInitialSyncCompleted(group, targetSlot);

            // Every other configured slot gets a brief catch-up dip too, but
            // only when this connect brings the *group* online. See
            // Umsetzungsplan.md, section "Sibling-Catch-up-Sweep: wann er
            // läuft und wann er abbricht".
            if (previousLeaderId is not null)
            {
                return;
            }

            var siblingSlots = group.Group.Slots.Where(s => s.Id != targetSlot.Id).ToList();
            if (siblingSlots.Count == 0)
            {
                return;
            }

            // Only now that the leader has connected is this pass's full
            // size known - see the SlotSyncBatchStarting note above.
            RaiseSlotSyncBatchStarting(siblingSlots.Count);
            _diagnosticLogger.Info($"[{group.Group.Name}] Catch-up sweep starting for {siblingSlots.Count} sibling slot(s)");

            for (var i = 0; i < siblingSlots.Count; i++)
            {
                var siblingSlot = siblingSlots[i];
                await CatchUpSyncCoreAsync(group, siblingSlot);
                RaiseSlotInitialSyncCompleted(group, siblingSlot);

                // Abort the rest of the sweep if this group's connection was
                // interrupted mid-pass. See Umsetzungsplan.md, section
                // "Sibling-Catch-up-Sweep: wann er läuft und wann er
                // abbricht".
                var stillOnline = _leaderSlotByGroup.TryGetValue(groupId, out var stillLeaderId) && stillLeaderId == targetSlot.Id;
                if (_autoReconnectSuppressed.ContainsKey(groupId) || !stillOnline)
                {
                    _diagnosticLogger.Warning($"[{group.Group.Name}] Catch-up sweep aborted after {i + 1}/{siblingSlots.Count} slot(s) - group no longer online");

                    // Every skipped slot still needs its "processed" signal
                    // so the progress indicator isn't left stuck short.
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

        // Cancel before touching _groupLocks, in this order. See
        // Umsetzungsplan.md, section "Disconnect: Reihenfolge Cancel vor
        // Lock".
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

            // A manual disconnect sticks across an app restart too - see
            // AutoConnect/PreferredLeaderSlotId. Posted to the UI thread for
            // the same reasons as in SwitchLeaderAsync.
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
                await _socketCleanup.CloseAsync(group, leaderSlot, session);
            }

            if (leaderSlot is not null)
            {
                _messageHistoryService.HandleDisconnected(group, leaderSlot, "disconnected by user");
                _diagnosticLogger.Info($"[{group.Group.Name}] Disconnected by user (was leader: {leaderSlot.DisplayName})");
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
        // Gated the same as SwitchLeaderAsync/DisconnectGroupAsync. See
        // Umsetzungsplan.md, section "CatchUpSyncAsync vs.
        // CatchUpSyncCoreAsync (Locking)".
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
    /// (already holding the group's gate). See Umsetzungsplan.md, section
    /// "CatchUpSyncAsync vs. CatchUpSyncCoreAsync (Locking)".
    /// </summary>
    private async Task CatchUpSyncCoreAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;

        // The slot may have been removed from the group's configuration
        // since this catch-up was queued - nothing left worth syncing then.
        if (!group.Group.Slots.Any(s => s.Id == slot.Id))
        {
            return;
        }

        // A manual Disconnect means no further catch-up dips for this group
        // either. See Umsetzungsplan.md, section "Disconnect: Reihenfolge
        // Cancel vor Lock".
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

            // Subscribe the already-open leader session to this slot's own
            // hint key too - see Umsetzungsplan.md, section "Warum die
            // Leader-Session pro Sibling-Slot ein eigenes TrackHints
            // braucht".
            if (_sessions.TryGetValue(leaderId, out var leaderSession))
            {
                _sessionEvents.TrackHintsForSiblingOnLeader(group, leaderSession, slot);
            }
        }

        if (_sessions.ContainsKey(slot.Id))
        {
            return; // a connection for this slot is already in flight/open.
        }

        using var connectCts = new CancellationTokenSource();
        _connectCancellationSources[groupId] = connectCts;
        IArchipelagoSession? session;
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

        // Give the backlog burst a moment to fully land before tearing this
        // temporary session back down.
        await Task.Delay(_itemBacklogGracePeriod);

        if (_sessions.TryRemove(slot.Id, out var stillTracked) && stillTracked == session)
        {
            await _socketCleanup.CloseAsync(group, slot, session);
        }
    }

    public async Task InitializeGroupAsync(GroupViewModel group)
    {
        // A group not set to auto-connect stays fully offline at startup -
        // zero network activity, not even a catch-up dip. If/when the user
        // reconnects, SwitchLeaderAsync itself catches every other
        // configured slot up as part of that connect.
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

    /// <summary>
    /// Serialized against SwitchLeaderAsync/DisconnectGroupAsync/CatchUpSyncAsync
    /// via the same per-group gate, so this method's own probe connect can't
    /// run concurrently with this group's gated leader connect.
    /// </summary>
    public async Task<IReadOnlyList<Archipelago.MultiClient.Net.Helpers.PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group)
    {
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Prefer an already-open session over opening a redundant extra
            // connection just to read the roster. See Umsetzungsplan.md,
            // section "Warum GetHintableItemsAsync/GetRoomPlayersAsync eine
            // offene Session bevorzugen".
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

            // Nothing connected right now - open a brief temporary probe
            // session for whichever slot is already configured, purely to
            // read the room's current player list.
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
                await _socketCleanup.CloseAsync(group, probeSlot, session);
            }

            return players;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Drops room-roster entries that can't be configured as a slot: slot 0
    /// (reserved for the server itself, commonly named "Server") and
    /// item-link group entries (<see cref="Archipelago.MultiClient.Net.Helpers.PlayerInfo.IsGroup"/>).
    /// </summary>
    // internal rather than private so AvaloniaApplication1.Tests can exercise this
    // session-less helper directly - no runtime behavior change, just test visibility.
    internal static IReadOnlyList<Archipelago.MultiClient.Net.Helpers.PlayerInfo> FilterToRealPlayers(
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

    public async Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group, SlotProfile slot)
    {
        // Fast path: no connection needed if this slot connected at least
        // once since app start. See Umsetzungsplan.md, section
        // "GetHintableLocationsAsync: Fast-Path und Re-Check".
        if (_sessionEvents.TryGetMissingLocations(slot.Id, out var cached))
        {
            return cached;
        }

        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Re-check with the gate held - a concurrent call may have
            // populated the cache while this one was waiting.
            if (_sessionEvents.TryGetMissingLocations(slot.Id, out cached))
            {
                return cached;
            }

            // Genuinely never connected - connect once (populates the cache
            // as a side effect), keeping the session alive for the rest of
            // this Hint-picker round; ReleaseHeldSessionAsync tears it down.
            await RunAsSlotAsync<object?>(group, slot, _ => Task.FromResult<object?>(null), keepAlive: true);

            return _sessionEvents.TryGetMissingLocations(slot.Id, out cached) ? cached : Array.Empty<HintableLocation>();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SendHintAsync(GroupViewModel group, SlotProfile slot, long locationId)
    {
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await RunAsSlotAsync<object?>(group, slot, session =>
            {
                // No player parameter - defaults to the requesting slot,
                // exactly what's wanted here.
                session.Hints.CreateHints(locationIds: new[] { locationId });
                return Task.FromResult<object?>(null);
            }, keepAlive: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SendItemHintAsync(GroupViewModel group, SlotProfile slot, string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return;
        }

        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await RunAsSlotAsync<object?>(group, slot, session =>
            {
                session.Say($"!hint {itemName}");
                return Task.FromResult<object?>(null);
            }, keepAlive: true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Reuses the group's already-open session when one exists, instead of
    /// connecting <paramref name="slot"/> itself. See Umsetzungsplan.md,
    /// section "Warum GetHintableItemsAsync/GetRoomPlayersAsync eine offene
    /// Session bevorzugen".
    /// </summary>
    public async Task<IReadOnlyList<string>> GetHintableItemsAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_leaderSlotByGroup.TryGetValue(groupId, out var leaderId) &&
                _sessions.TryGetValue(leaderId, out var leaderSession))
            {
                return await FetchHintableItemsViaSessionAsync(groupId, slot, leaderSession, leaderId);
            }

            var result = await RunAsSlotAsync<IReadOnlyList<string>>(
                group, slot,
                session => FetchHintableItemsViaSessionAsync(groupId, slot, session, slot.Id),
                keepAlive: true);

            return result ?? Array.Empty<string>();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Does the game-lookup/checksum-check/DataPackage-fetch work for
    /// <see cref="GetHintableItemsAsync"/> against whichever session it was
    /// handed - not necessarily <paramref name="slot"/>'s own.
    /// <paramref name="roomInfoOwnerSlotId"/> is whichever slot's
    /// <see cref="_roomInfoBySlot"/> entry to read.
    /// </summary>
    private async Task<IReadOnlyList<string>> FetchHintableItemsViaSessionAsync(
        Guid groupId, SlotProfile slot, IArchipelagoSession session, Guid roomInfoOwnerSlotId)
    {
        var ownGame = session.Players.AllPlayers
            .FirstOrDefault(p => string.Equals(p.Name, slot.SlotName, StringComparison.OrdinalIgnoreCase))?.Game;
        if (string.IsNullOrEmpty(ownGame))
        {
            return Array.Empty<string>();
        }

        _roomInfoBySlot.TryGetValue(roomInfoOwnerSlotId, out var roomInfo);
        var currentChecksum = roomInfo?.DataPackageChecksums is { } checksums && checksums.TryGetValue(ownGame, out var checksum)
            ? checksum
            : null;

        var cached = _persistenceService?.LoadDataPackageCache(groupId, ownGame);
        if (cached is not null && currentChecksum is not null &&
            string.Equals(cached.Checksum, currentChecksum, StringComparison.Ordinal))
        {
            return cached.ItemNames;
        }

        var itemNames = await FetchItemNamesFromDataPackageAsync(session, ownGame, _dataPackageRequestTimeout);

        // Only worth caching with a checksum to validate it against later -
        // otherwise every future call would treat it as stale anyway.
        if (itemNames.Count > 0 && currentChecksum is not null)
        {
            _persistenceService?.SaveDataPackageCache(groupId, ownGame,
                new DataPackageCacheEntry { Checksum = currentChecksum, ItemNames = itemNames.ToList() });
        }

        return itemNames;
    }

    /// <summary>
    /// Sends the raw <see cref="GetDataPackagePacket"/> and awaits the
    /// matching <see cref="DataPackagePacket"/> directly. See
    /// Umsetzungsplan.md, section "DataPackage-Abruf per Rohpaket".
    /// </summary>
    private static async Task<IReadOnlyList<string>> FetchItemNamesFromDataPackageAsync(IArchipelagoSession session, string game, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPacketReceived(ArchipelagoPacketBase packet)
        {
            if (packet is DataPackagePacket dataPackagePacket)
            {
                var names = dataPackagePacket.DataPackage.Games.TryGetValue(game, out var gameData)
                    ? (IReadOnlyList<string>)gameData.ItemLookup.Keys.ToList()
                    : Array.Empty<string>();
                tcs.TrySetResult(names);
            }
        }

        void OnSocketClosed(string reason) => tcs.TrySetResult(Array.Empty<string>());

        session.Socket.PacketReceived += OnPacketReceived;
        session.Socket.SocketClosed += OnSocketClosed;
        try
        {
            session.Socket.SendPacket(new GetDataPackagePacket { Games = new[] { game } });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            return completed == tcs.Task ? await tcs.Task : Array.Empty<string>();
        }
        catch (Exception)
        {
            // Socket already closed/errored trying to send.
            return Array.Empty<string>();
        }
        finally
        {
            session.Socket.PacketReceived -= OnPacketReceived;
            session.Socket.SocketClosed -= OnSocketClosed;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> against a session for exactly
    /// <paramref name="slot"/>, reusing an open one if available, otherwise
    /// briefly connecting. Callers must already hold this group's
    /// <see cref="_groupLocks"/> gate. See Umsetzungsplan.md, section
    /// "RunAsSlotAsync / ReleaseHeldSessionAsync (Hint-Picker)".
    /// </summary>
    /// <param name="keepAlive">
    /// Leave a brand-new non-leader session open afterward instead of
    /// disconnecting it immediately; ignored if an existing session was
    /// reused. See Umsetzungsplan.md, section "RunAsSlotAsync /
    /// ReleaseHeldSessionAsync (Hint-Picker)".
    /// </param>
    private async Task<T?> RunAsSlotAsync<T>(GroupViewModel group, SlotProfile slot, Func<IArchipelagoSession, Task<T>> action, bool keepAlive = false)
    {
        if (_sessions.TryGetValue(slot.Id, out var existingSession))
        {
            return await action(existingSession);
        }

        var session = await ConnectSlotSessionAsync(group, slot, isLeaderSession: false);
        if (session is null)
        {
            return default;
        }

        if (keepAlive)
        {
            // Left registered in _sessions (ConnectSlotSessionAsync already
            // put it there) - ReleaseHeldSessionAsync tears it down later.
            return await action(session);
        }

        try
        {
            return await action(session);
        }
        finally
        {
            if (_sessions.TryRemove(slot.Id, out var stillTracked) && stillTracked == session)
            {
                await _socketCleanup.CloseAsync(group, slot, session);
            }
        }
    }

    /// <summary>
    /// Disconnects a kept-alive Hint-picker probe session for
    /// <paramref name="slot"/> once the picker moves on from it. No-op if
    /// <paramref name="slot"/> is the group's leader, or if nothing is held
    /// for it.
    /// </summary>
    public async Task ReleaseHeldSessionAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_leaderSlotByGroup.TryGetValue(groupId, out var leaderId) && leaderId == slot.Id)
            {
                return;
            }

            if (_sessions.TryRemove(slot.Id, out var session))
            {
                await _socketCleanup.CloseAsync(group, slot, session);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Whether <paramref name="slot"/> has no known way to satisfy a
    /// password it's believed to need. See Umsetzungsplan.md, section
    /// "Passwort-Prompt und Retry-Flow".
    /// </summary>
    private static bool NeedsPasswordPrompt(GroupViewModel group, SlotProfile slot) =>
        slot.RequiresPassword && string.IsNullOrEmpty(slot.Password) && string.IsNullOrEmpty(group.Group.Password);

    /// <summary>
    /// Updates <see cref="SlotProfile.RequiresPassword"/> and, if it
    /// changed, raises <see cref="GroupPersistNeeded"/> so the belief
    /// survives a restart.
    /// </summary>
    private void UpdateRequiresPassword(GroupViewModel group, SlotProfile slot, bool requiresPassword)
    {
        if (slot.RequiresPassword == requiresPassword)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            slot.RequiresPassword = requiresPassword;
            GroupPersistNeeded?.Invoke(group);
        });
    }

    /// <summary>
    /// Opens and logs in a session for <paramref name="slot"/> and wires up
    /// its event handlers. Used for both leader connections (which stay
    /// open) and catch-up dips (<paramref name="isLeaderSession"/> = false,
    /// torn down by the caller shortly after this returns). Returns null
    /// (having already reported the failure) if the connection or login
    /// fails. See Umsetzungsplan.md, sections "Transiente
    /// Verbindungsfehler und Retry" and "Passwort-Prompt und Retry-Flow".
    /// </summary>
    private async Task<IArchipelagoSession?> ConnectSlotSessionAsync(GroupViewModel group, SlotProfile slot, bool isLeaderSession, CancellationToken cancellationToken = default)
    {
        // Up to MaxTransientConnectRetries extra attempts with a fresh
        // session on a transient-looking failure - see
        // IsTransientConnectFailure and Umsetzungsplan.md, section
        // "Transiente Verbindungsfehler und Retry".
        for (var attempt = 1; attempt <= 1 + MaxTransientConnectRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            IArchipelagoSession session;
            try
            {
                session = _sessionFactory.CreateSession(group.Group.Host, group.Group.Port);
            }
            catch (Exception ex)
            {
                if (isLeaderSession)
                {
                    SetConnectionState(group, ConnectionState.Error);
                }

                _messageHistoryService.HandleError(group, $"Could not create session for {slot.DisplayName}: {ex.Message}");
                _diagnosticLogger.Error($"[{group.Group.Name}] Could not create session for {slot.DisplayName}", ex);
                return null;
            }

            // Set once the backlog grace period elapses after a successful
            // leader login; read by OnItemReceived. See Umsetzungsplan.md,
            // section "Warum der Item-Backlog eine Grace-Period braucht".
            // Declared fresh each attempt so a retry's closures capture its
            // own flag.
            var hasAnnouncedConnection = false;

            session.Socket.SocketClosed += reason => OnSocketClosed(group, slot, reason);
            session.Socket.ErrorReceived += (ex, message) =>
            {
                if (SocketCleanup.IsExpectedSendQueueCompletionError(ex))
                {
                    // Our own CompleteSendQueue() workaround deliberately
                    // provokes this - see Umsetzungsplan.md, section
                    // "Erwartete Exceptions durch die eigenen Workarounds".
                    return;
                }

                if (SocketCleanup.IsMalformedBounceDataError(ex))
                {
                    // A protocol violation by another client, not us - see
                    // Umsetzungsplan.md, section "Erwartete Exceptions durch
                    // die eigenen Workarounds".
                    _diagnosticLogger.Warning($"[{group.Group.Name}] [{slot.DisplayName}] Rejected malformed Bounce packet: {ex.Message}");
                    return;
                }

                _messageHistoryService.HandleError(group, $"[{slot.DisplayName}] {message}");
                _diagnosticLogger.Warning($"[{group.Group.Name}] [{slot.DisplayName}] {message}");
            };

            if (isLeaderSession)
            {
                // Only the leader's session stays open long enough for this
                // to matter - see SessionEventTranslator.OnLeaderMessageReceived.
                session.MessageLog.OnMessageReceived += logMessage => _sessionEvents.OnLeaderMessageReceived(group, session, slot, logMessage);
            }

            // Clear this slot's received-items panel before subscribing -
            // the server re-delivers the full item history on every connect.
            _messageHistoryService.ClearReceivedItemsForSlot(group, slot.Id);

            session.Items.ItemReceived += helper => _sessionEvents.OnItemReceived(group, slot, session, helper, isLeaderSession, hasAnnouncedConnection);

            try
            {
                // Proactive password prompt, before opening the socket. See
                // Umsetzungsplan.md, section "Passwort-Prompt und
                // Retry-Flow".
                if (NeedsPasswordPrompt(group, slot) && PasswordRequested is not null)
                {
                    var obtained = await PasswordRequested(group, slot, false /* isRetryAfterFailure */, cancellationToken);
                    if (!obtained || cancellationToken.IsCancellationRequested)
                    {
                        if (isLeaderSession)
                        {
                            SetConnectionState(group, ConnectionState.Disconnected);
                        }

                        return null; // declined/cancelled - quiet skip, not a real error.
                    }
                }

                var roomInfo = await session.ConnectAsync();
                if (roomInfo is not null)
                {
                    // Lets GetHintableItemsAsync read DataPackageChecksums
                    // without a separate request.
                    _roomInfoBySlot[slot.Id] = roomInfo;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // Disconnected while the connect itself was in flight -
                    // skip logging in and tear the attempt back down.
                    await _socketCleanup.CloseAsync(group, slot, session);
                    return null;
                }

                // Most rooms share one password; slot.Password is only set
                // for a custom-hosted room's per-slot override.
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
                    await _socketCleanup.CloseAsync(group, slot, session);
                    return null;
                }

                if (loginResult is not LoginSuccessful)
                {
                    var failure = (LoginFailure)loginResult;

                    // InvalidPassword is the one LoginFailure reason handled
                    // specially - see Umsetzungsplan.md, section
                    // "Passwort-Prompt und Retry-Flow".
                    if (failure.ErrorCodes.Contains(ConnectionRefusedError.InvalidPassword))
                    {
                        UpdateRequiresPassword(group, slot, true);

                        if (PasswordRequested is not null)
                        {
                            await _socketCleanup.CloseAsync(group, slot, session);

                            var provided = await PasswordRequested(group, slot, true /* isRetryAfterFailure */, cancellationToken);
                            if (provided && !cancellationToken.IsCancellationRequested)
                            {
                                attempt--; // a password retry doesn't consume a MaxTransientConnectRetries slot.
                                continue;
                            }

                            if (isLeaderSession)
                            {
                                SetConnectionState(group, ConnectionState.Disconnected);
                            }

                            return null; // declined/cancelled - quiet skip, not a real error.
                        }
                    }

                    var errorText = string.Join("; ", failure.Errors);
                    if (isLeaderSession)
                    {
                        SetConnectionState(group, ConnectionState.Error);
                    }

                    _messageHistoryService.HandleError(group, $"Login failed for {slot.DisplayName}: {errorText}");
                    _diagnosticLogger.Warning($"[{group.Group.Name}] Login rejected for {slot.DisplayName}: {errorText}");
                    await _socketCleanup.CloseAsync(group, slot, session);
                    return null; // a real rejection, not a transient hiccup - never retried.
                }

                // Login succeeded - learn whether a password was actually
                // needed from what was used. See Umsetzungsplan.md, section
                // "Passwort-Prompt und Retry-Flow".
                UpdateRequiresPassword(group, slot, !string.IsNullOrEmpty(effectivePassword));
            }
            catch (Exception ex) when (IsTransientConnectFailure(ex) && attempt <= MaxTransientConnectRetries)
            {
                // This attempt's session is now indeterminate - discard and
                // retry with a fresh one.
                await _socketCleanup.CloseAsync(group, slot, session);
                _messageHistoryService.HandleError(
                    group,
                    $"Connecting {slot.DisplayName} hit a transient error ({ex.GetType().Name}), retrying ({attempt}/{MaxTransientConnectRetries})...");
                _diagnosticLogger.Warning($"[{group.Group.Name}] Transient connect error for {slot.DisplayName} ({ex.GetType().Name}), retrying ({attempt}/{MaxTransientConnectRetries})");

                try
                {
                    await Task.Delay(_transientConnectRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled during the backoff pause - stop retrying.
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

                _messageHistoryService.HandleError(group, $"Connection failed for {slot.DisplayName}: {ex.Message}");
                _diagnosticLogger.Error($"[{group.Group.Name}] Connection failed for {slot.DisplayName}", ex);
                await _socketCleanup.CloseAsync(group, slot, session);
                return null;
            }

            _sessions[slot.Id] = session;

            // Tier 1 of Fortschrittsanzeigen.md: "X of Y locations checked".
            // Populated after every successful login; kept live afterward
            // only for the leader.
            SessionEventTranslator.UpdateLocationProgress(slot, session);

            // Hint picker's Location-mode missing-locations cache - same
            // "populate now, keep live for the leader" shape as above.
            _sessionEvents.UpdateMissingLocationsCache(slot, session);

            if (isLeaderSession)
            {
                session.Locations.CheckedLocationsUpdated += _ =>
                {
                    SessionEventTranslator.UpdateLocationProgress(slot, session);
                    _sessionEvents.UpdateMissingLocationsCache(slot, session);
                };
            }

            // DeathLink: only the leader gets it. See Umsetzungsplan.md,
            // section "DeathLink".
            if (isLeaderSession)
            {
                var deathLinkService = _sessionFactory.CreateDeathLinkService(session);
                if (deathLinkService is not null)
                {
                    deathLinkService.EnableDeathLink();
                    deathLinkService.OnDeathLinkReceived += deathLink => _messageHistoryService.HandleDeathLinkReceived(group, deathLink);
                }
            }

            // Fires with this slot's currently unlocked hints, then again on
            // every later change. A hint notification is not room-wide - see
            // Umsetzungsplan.md, section "Warum die Leader-Session pro
            // Sibling-Slot ein eigenes TrackHints braucht".
            session.Hints.TrackHints(
                hints => _sessionEvents.OnHintsUpdated(group, session, hints),
                retrieveCurrentlyUnlockedHints: true);

            if (isLeaderSession)
            {
                // Track every other configured slot's hints too, over this
                // same connection - see Umsetzungsplan.md, section above.
                var roster = SessionEventTranslator.BuildSlotRoster(session, group.Group);
                foreach (var (numericSlotId, siblingSlot) in roster)
                {
                    if (siblingSlot.Id == slot.Id)
                    {
                        continue; // this leader slot is already covered above
                    }

                    session.Hints.TrackHints(
                        hints => _sessionEvents.OnHintsUpdated(group, session, hints),
                        retrieveCurrentlyUnlockedHints: true,
                        slot: numericSlotId);
                }

                _messageHistoryService.HandleConnected(group, slot);
                _diagnosticLogger.Info($"[{group.Group.Name}] Connected as leader: {slot.DisplayName}");
            }

            _ = Task.Delay(_itemBacklogGracePeriod)
                    .ContinueWith(_ => hasAnnouncedConnection = true, TaskScheduler.Default);

            return session;
        }

        // Unreachable: every loop iteration either returns or, on its last
        // allowed attempt, falls into the plain catch block above and
        // returns null instead of looping again.
        return null;
    }

    /// <summary>
    /// Exceptions that look like a transient network/timing hiccup rather
    /// than a real rejection. See Umsetzungsplan.md, section "Transiente
    /// Verbindungsfehler und Retry".
    /// </summary>
    // internal rather than private so AvaloniaApplication1.Tests can exercise this
    // session-less helper directly - no runtime behavior change, just test visibility.
    internal static bool IsTransientConnectFailure(Exception ex) =>
        ex is TaskCanceledException or OperationCanceledException or TimeoutException;

    private void OnSocketClosed(GroupViewModel group, SlotProfile slot, string reason)
    {
        var wasConnected = _sessions.TryRemove(slot.Id, out _);
        if (!wasConnected)
        {
            // Either the handshake was still pending, or this is a
            // deliberate close that already handled its own bookkeeping.
            return;
        }

        var groupId = group.Group.Id;
        var isStillTheLeader = _leaderSlotByGroup.TryGetValue(groupId, out var leaderId) && leaderId == slot.Id;

        if (!isStillTheLeader)
        {
            // A catch-up session that closed itself before the caller's own
            // teardown ran, or bookkeeping that already moved on.
            return;
        }

        _messageHistoryService.HandleDisconnected(group, slot, reason);
        _leaderSlotByGroup.TryRemove(groupId, out _);
        Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(null, null));

        if (!_autoReconnectSuppressed.ContainsKey(groupId) && group.Group.AutoConnect)
        {
            _diagnosticLogger.Warning($"[{group.Group.Name}] Leader {slot.DisplayName} dropped unexpectedly ({reason}), scheduling reconnect");
            SetConnectionState(group, ConnectionState.Reconnecting);
            _ = ScheduleReconnectAsync(group, slot);
        }
        else
        {
            _diagnosticLogger.Info($"[{group.Group.Name}] Leader {slot.DisplayName} dropped ({reason}), not auto-reconnecting");
            SetConnectionState(group, ConnectionState.Disconnected);
        }
    }

    /// <summary>
    /// Tries to bring the leader back as the same slot that just dropped -
    /// not necessarily <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>,
    /// which only decides who connects at startup. See Umsetzungsplan.md,
    /// section "Reconnect-Scheduling nach unerwartetem Drop".
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

    private static void SetConnectionState(GroupViewModel group, ConnectionState state) =>
        Dispatcher.UIThread.Post(() => group.ConnectionState = state);
}
