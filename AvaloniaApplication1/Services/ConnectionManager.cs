using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// Owns, per <see cref="ServerConnectionGroup"/>, at most one persistent session (the "leader")
/// plus short-lived catch-up/probe sessions for non-leader slots. Design: Umsetzungsplan.md,
/// Phase 6; extended rationale in its "ConnectionManager: Locking, Nebenläufigkeit und
/// Workarounds im Detail" section - comments below point at subsections by name.
/// </summary>
public class ConnectionManager : IConnectionManager
{
    // The last value repeats for further attempts.
    private static readonly int[] ReconnectDelaysSeconds = { 5, 10, 30 };

    // Per group, not per slot, so a restart doesn't burst logins at every server at once.
    public static readonly TimeSpan StartupGroupSpacing = TimeSpan.FromSeconds(3);

    // See Umsetzungsplan.md, "Warum der Item-Backlog eine Grace-Period braucht".
    private static readonly TimeSpan DefaultItemBacklogGracePeriod = TimeSpan.FromSeconds(2);

    // Network-protocol version for the login handshake, not this app's version.
    private static readonly Version ArchipelagoProtocolVersion = new(0, 6, 7);

    private const int MaxTransientConnectRetries = 2;

    private static readonly TimeSpan DefaultTransientConnectRetryDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan DefaultDataPackageRequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan DefaultConnectGateMaxHold = TimeSpan.FromSeconds(30);

    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;
    private readonly ISessionFactory _sessionFactory;

    // Optional; null means the DataPackage is always fetched fresh instead of cached on disk.
    private readonly IPersistenceService? _persistenceService;

    private readonly IDiagnosticLogger _diagnosticLogger;

    private readonly TimeSpan _dataPackageRequestTimeout;

    // See Umsetzungsplan.md, "Item-Backlog-Grace-Period und Retry-Delay (Testbarkeit)".
    private readonly TimeSpan _itemBacklogGracePeriod;
    private readonly TimeSpan _transientConnectRetryDelay;

    // Keyed by SlotProfile.Id. See Umsetzungsplan.md, "_sessions: Lifecycle und maximale gleichzeitige Einträge".
    private readonly ConcurrentDictionary<Guid, IArchipelagoSession> _sessions = new();

    private readonly SessionEventTranslator _sessionEvents;
    private readonly SocketCleanup _socketCleanup;

    // App-wide, not per group. See Umsetzungsplan.md, "Globale Connect-Sperre und Catch-up-Queue".
    private readonly ConnectGate _connectGate;
    private readonly CatchUpQueue _catchUpQueue;

    // Keyed by SlotProfile.Id. See Umsetzungsplan.md, "_roomInfoBySlot".
    private readonly ConcurrentDictionary<Guid, RoomInfoPacket> _roomInfoBySlot = new();

    // GroupId -> leader SlotProfile.Id; absent = no leader right now.
    private readonly ConcurrentDictionary<Guid, Guid> _leaderSlotByGroup = new();

    // Serializes every connect for one server, so no two can race.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _groupLocks = new();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reconnectTokens = new();

    // See Umsetzungsplan.md, "_connectCancellationSources".
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _connectCancellationSources = new();

    // See Umsetzungsplan.md, "_autoReconnectSuppressed".
    private readonly ConcurrentDictionary<Guid, bool> _autoReconnectSuppressed = new();

    /// <inheritdoc/>
    public event Action<GroupViewModel>? GroupPersistNeeded;

    /// <inheritdoc/>
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    /// <inheritdoc/>
    public event Action<int>? SlotSyncBatchStarting;

    /// <inheritdoc/>
    public event Action<GroupViewModel, ServerReplyKind, string>? ServerReplyReceived;

    /// <inheritdoc/>
    public event Action<GroupViewModel>? LeaderConnected;

    /// <inheritdoc/>
    public Func<GroupViewModel, SlotProfile, bool, CancellationToken, Task<bool>>? PasswordRequested { get; set; }

    // Optional. Used directly only for DeathLink; item/hint/chat triggers live in SessionEventTranslator/HintService.
    private readonly IAttentionTracker? _attentionTracker;

    public ConnectionManager(
        IMessageHistoryService messageHistoryService,
        IHintService hintService,
        ISessionFactory sessionFactory,
        TimeSpan? itemBacklogGracePeriod = null,
        TimeSpan? transientConnectRetryDelay = null,
        IPersistenceService? persistenceService = null,
        TimeSpan? dataPackageRequestTimeout = null,
        IDiagnosticLogger? diagnosticLogger = null,
        IAttentionTracker? attentionTracker = null,
        TimeSpan? connectGateMaxHold = null)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
        _sessionFactory = sessionFactory;
        _itemBacklogGracePeriod = itemBacklogGracePeriod ?? DefaultItemBacklogGracePeriod;
        _transientConnectRetryDelay = transientConnectRetryDelay ?? DefaultTransientConnectRetryDelay;
        _persistenceService = persistenceService;
        _dataPackageRequestTimeout = dataPackageRequestTimeout ?? DefaultDataPackageRequestTimeout;
        _diagnosticLogger = diagnosticLogger ?? NullDiagnosticLogger.Instance;
        _attentionTracker = attentionTracker;
        _sessionEvents = new SessionEventTranslator(_messageHistoryService, _hintService, _attentionTracker);
        _socketCleanup = new SocketCleanup(_messageHistoryService, _diagnosticLogger);
        _connectGate = new ConnectGate(connectGateMaxHold ?? DefaultConnectGateMaxHold);
        _catchUpQueue = new CatchUpQueue(ProcessQueuedCatchUpAsync, RaiseSlotSyncBatchStarting, RaiseSlotInitialSyncCompleted);
    }

    public async Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot)
    {
        var groupId = group.Group.Id;
        using var groupLock = await LockGroupAsync(groupId);

        if (IsLeader(groupId, targetSlot.Id))
        {
            return;
        }

        CancelPendingReconnect(groupId);
        _autoReconnectSuppressed.TryRemove(groupId, out _);

        SetConnectionState(group, ConnectionState.Connecting);

        // See Umsetzungsplan.md, "SlotSyncBatchStarting: warum erst 1, dann die Sibling-Anzahl".
        RaiseSlotSyncBatchStarting(1);

        // New leader first, old one closed only after. See Umsetzungsplan.md, "Leader-Wechsel: bewusste kurze Überlappung".
        var newSession = await ConnectCancellableAsync(group, targetSlot, isLeaderSession: true);
        if (newSession is null)
        {
            RestoreLeaderStateAfterFailedSwitch(group, targetSlot);
            return;
        }

        var previousLeaderId = GetLeaderId(groupId);
        PromoteToLeader(group, targetSlot);
        _ = ReadGoalStatesAsync(group, newSession);
        await CloseOutgoingLeaderAsync(group, previousLeaderId, targetSlot);

        // See Umsetzungsplan.md, "Sibling-Catch-up: wann er läuft und wann er abbricht".
        var groupJustCameOnline = previousLeaderId is null;
        if (groupJustCameOnline)
        {
            EnqueueSiblingCatchUps(group, targetSlot);
        }

        // The leader already got its own full backlog via login. Raised after the siblings are
        // announced, so the banner never briefly shows a finished pass in between.
        RaiseSlotInitialSyncCompleted(group, targetSlot);
    }

    /// <summary>A plain request on the open session, not a handshake, so it needs neither the group lock nor the connect gate.</summary>
    private async Task ReadGoalStatesAsync(GroupViewModel group, IArchipelagoSession session)
    {
        try
        {
            await _sessionEvents.ReadGoalStatesAsync(group, session);
        }
        catch (Exception ex)
        {
            _diagnosticLogger.Warning($"[{group.Group.Name}] Could not read goal states: {ex.Message}");
        }
    }

    /// <summary>The error was already reported; the previous leader was never touched, so point the dropdown back at it.</summary>
    private void RestoreLeaderStateAfterFailedSwitch(GroupViewModel group, SlotProfile failedSlot)
    {
        var stillLeaderId = GetLeaderId(group.Group.Id);
        var stillLeaderSlot = stillLeaderId is null ? null : FindSlot(group, stillLeaderId.Value);
        PostLeaderState(group, stillLeaderId, stillLeaderSlot);

        // Closes out the announced 1-slot batch so progress isn't left stuck.
        RaiseSlotInitialSyncCompleted(group, failedSlot);
    }

    private void PromoteToLeader(GroupViewModel group, SlotProfile slot)
    {
        _leaderSlotByGroup[group.Group.Id] = slot.Id;
        PostLeaderState(group, slot.Id, slot);
        SetConnectionState(group, ConnectionState.Connected);

        // Posted after PostLeaderState so subscribers see the new leader. See Umsetzungsplan.md,
        // "Admin-Ansicht: ServerReplyReceived und LeaderConnected".
        Dispatcher.UIThread.Post(() => LeaderConnected?.Invoke(group));

        RememberAsAutoConnectLeader(group, slot);
    }

    /// <summary>Any successful leader connect brings the group back at next start - no separate opt-in.</summary>
    private void RememberAsAutoConnectLeader(GroupViewModel group, SlotProfile slot)
    {
        if (group.Group.AutoConnect && group.Group.PreferredLeaderSlotId == slot.Id)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            group.Group.AutoConnect = true;
            group.Group.PreferredLeaderSlotId = slot.Id;
            GroupPersistNeeded?.Invoke(group);
        });
    }

    /// <summary>A manual disconnect sticks across an app restart too.</summary>
    private void ForgetAutoConnect(GroupViewModel group)
    {
        if (!group.Group.AutoConnect)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            group.Group.AutoConnect = false;
            GroupPersistNeeded?.Invoke(group);
        });
    }

    private async Task CloseOutgoingLeaderAsync(GroupViewModel group, Guid? previousLeaderId, SlotProfile newLeader)
    {
        if (previousLeaderId is null || previousLeaderId == newLeader.Id ||
            !_sessions.TryRemove(previousLeaderId.Value, out var oldSession))
        {
            return;
        }

        var previousSlot = FindSlot(group, previousLeaderId.Value);
        if (previousSlot is not null)
        {
            _messageHistoryService.HandleDisconnected(group, previousSlot, "switched account");
            _diagnosticLogger.Info($"[{group.Group.Name}] Switched leader from {previousSlot.DisplayName} to {newLeader.DisplayName}");
        }

        await _socketCleanup.CloseAsync(group, previousSlot, oldSession);
    }

    /// <summary>Appended to the app-wide queue; the caller doesn't wait for them.</summary>
    private void EnqueueSiblingCatchUps(GroupViewModel group, SlotProfile leaderSlot)
    {
        var siblingSlots = group.Group.Slots.Where(s => s.Id != leaderSlot.Id).ToList();
        if (siblingSlots.Count == 0)
        {
            return;
        }

        _diagnosticLogger.Info($"[{group.Group.Name}] Queueing catch-up for {siblingSlots.Count} sibling slot(s)");
        _ = _catchUpQueue.EnqueueAsync(group, siblingSlots);
    }

    /// <summary>Takes the group's gate per slot, so other work on that server can run in between.</summary>
    private async Task ProcessQueuedCatchUpAsync(GroupViewModel group, SlotProfile slot)
    {
        try
        {
            using var groupLock = await LockGroupAsync(group.Group.Id);

            // See Umsetzungsplan.md, "Sibling-Catch-up: wann er läuft und wann er abbricht".
            if (!IsOnline(group.Group.Id))
            {
                return;
            }

            await CatchUpSyncCoreAsync(group, slot);
        }
        catch (Exception ex)
        {
            _diagnosticLogger.Error($"[{group.Group.Name}] Catch-up failed for {slot.DisplayName}", ex);
        }
    }

    private bool IsOnline(Guid groupId) =>
        !_autoReconnectSuppressed.ContainsKey(groupId) && GetLeaderId(groupId) is not null;

    /// <summary>Test hook: completes once no catch-up is pending or in flight.</summary>
    internal Task WhenCatchUpQueueIdleAsync() => _catchUpQueue.WhenIdleAsync();

    public async Task DisconnectGroupAsync(GroupViewModel group)
    {
        var groupId = group.Group.Id;

        // See Umsetzungsplan.md, "Disconnect: Reihenfolge Cancel vor Lock".
        _autoReconnectSuppressed[groupId] = true;
        if (_connectCancellationSources.TryGetValue(groupId, out var inFlightCts))
        {
            inFlightCts.Cancel();
        }

        _catchUpQueue.RemoveGroup(groupId);

        using var groupLock = await LockGroupAsync(groupId);

        CancelPendingReconnect(groupId);
        ForgetAutoConnect(group);

        if (_leaderSlotByGroup.TryRemove(groupId, out var leaderSlotId))
        {
            await CloseLeaderOnUserDisconnectAsync(group, leaderSlotId);
        }

        SetConnectionState(group, ConnectionState.Disconnected);
        PostLeaderState(group, null, null);
    }

    private async Task CloseLeaderOnUserDisconnectAsync(GroupViewModel group, Guid leaderSlotId)
    {
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
    }

    public Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot) =>
        _catchUpQueue.EnqueueAsync(group, new[] { slot });

    /// <summary>Caller holds the group's gate. See Umsetzungsplan.md, "CatchUpSyncAsync vs. CatchUpSyncCoreAsync (Locking)".</summary>
    private async Task CatchUpSyncCoreAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;

        var slotWasRemoved = !group.Group.Slots.Any(s => s.Id == slot.Id);
        var userDisconnected = _autoReconnectSuppressed.ContainsKey(groupId);
        if (slotWasRemoved || userDisconnected || IsLeader(groupId, slot.Id))
        {
            return;
        }

        // See Umsetzungsplan.md, "Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht".
        if (TryGetLeaderSession(groupId, out _, out var leaderSession))
        {
            _sessionEvents.TrackHintsForSiblingOnLeader(group, leaderSession, slot);
        }

        var connectionAlreadyOpen = _sessions.ContainsKey(slot.Id);
        if (connectionAlreadyOpen)
        {
            return;
        }

        var session = await ConnectCancellableAsync(group, slot, isLeaderSession: false, isBackgroundCatchUp: true);
        if (session is null)
        {
            return;
        }

        // Let the backlog burst fully land before tearing the session down.
        await Task.Delay(_itemBacklogGracePeriod);
        await CloseTemporarySessionAsync(group, slot, session);
    }

    public async Task InitializeGroupAsync(GroupViewModel group)
    {
        // No AutoConnect = zero network activity at startup; a later reconnect catches every slot up.
        if (!group.Group.AutoConnect || group.Group.ResolveStartupLeader() is not { } startupLeader)
        {
            return;
        }

        await SwitchLeaderAsync(group, startupLeader);
    }

    private void RaiseSlotInitialSyncCompleted(GroupViewModel group, SlotProfile slot) =>
        Dispatcher.UIThread.Post(() => SlotInitialSyncCompleted?.Invoke(group, slot));

    private void RaiseSlotSyncBatchStarting(int slotCount) =>
        Dispatcher.UIThread.Post(() => SlotSyncBatchStarting?.Invoke(slotCount));

    /// <summary>Gated so its probe connect can't run concurrently with the group's leader connect.</summary>
    public async Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group)
    {
        using var groupLock = await LockGroupAsync(group.Group.Id);

        // See Umsetzungsplan.md, "Warum GetHintableItemsAsync/GetRoomPlayersAsync eine offene Session bevorzugen".
        if (TryGetAnyOpenSession(group, out var openSession))
        {
            return FilterToRealPlayers(openSession.Players.AllPlayers);
        }

        var probeSlot = group.Group.Slots.FirstOrDefault();
        if (probeSlot is null)
        {
            return Array.Empty<PlayerInfo>();
        }

        var session = await ConnectSlotSessionAsync(group, probeSlot, isLeaderSession: false);
        if (session is null)
        {
            return Array.Empty<PlayerInfo>();
        }

        var players = FilterToRealPlayers(session.Players.AllPlayers);
        await CloseTemporarySessionAsync(group, probeSlot, session);
        return players;
    }

    private bool TryGetAnyOpenSession(GroupViewModel group, [NotNullWhen(true)] out IArchipelagoSession? session)
    {
        if (TryGetLeaderSession(group.Group.Id, out _, out session))
        {
            return true;
        }

        foreach (var slot in group.Group.Slots)
        {
            if (_sessions.TryGetValue(slot.Id, out session))
            {
                return true;
            }
        }

        session = null;
        return false;
    }

    /// <summary>Drops slot 0 (the server itself) and item-link group entries - neither is a loggable-into player.</summary>
    // internal rather than private so AvaloniaApplication1.Tests can exercise this.
    internal static IReadOnlyList<PlayerInfo> FilterToRealPlayers(
        IEnumerable<PlayerInfo> players) =>
        players.Where(p => p.Slot != 0 && !p.IsGroup).ToList();

    public Task SendMessageAsync(GroupViewModel group, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && TryGetLeaderSession(group.Group.Id, out _, out var session))
        {
            session.Say(text);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group, SlotProfile slot)
    {
        // See Umsetzungsplan.md, "GetHintableLocationsAsync: Fast-Path und Re-Check".
        if (_sessionEvents.TryGetMissingLocations(slot.Id, out var cached))
        {
            return cached;
        }

        using var groupLock = await LockGroupAsync(group.Group.Id);

        // A concurrent call may have populated the cache while this one waited.
        if (_sessionEvents.TryGetMissingLocations(slot.Id, out cached))
        {
            return cached;
        }

        // Connecting populates the cache as a side effect.
        await RunAsSlotAsync(group, slot, _ => { }, keepAlive: true);

        return _sessionEvents.TryGetMissingLocations(slot.Id, out cached) ? cached : Array.Empty<HintableLocation>();
    }

    public async Task SendHintAsync(GroupViewModel group, SlotProfile slot, long locationId)
    {
        using var groupLock = await LockGroupAsync(group.Group.Id);

        // No player parameter: defaults to the requesting slot.
        await RunAsSlotAsync(group, slot, session => session.Hints.CreateHints(locationIds: new[] { locationId }), keepAlive: true);
    }

    public async Task SendItemHintAsync(GroupViewModel group, SlotProfile slot, string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return;
        }

        using var groupLock = await LockGroupAsync(group.Group.Id);
        await RunAsSlotAsync(group, slot, session => session.Say($"!hint {itemName}"), keepAlive: true);
    }

    /// <summary>
    /// Prefers the leader's open session over connecting <paramref name="slot"/>. See Umsetzungsplan.md,
    /// "Warum GetHintableItemsAsync/GetRoomPlayersAsync eine offene Session bevorzugen".
    /// </summary>
    public async Task<IReadOnlyList<string>> GetHintableItemsAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;
        using var groupLock = await LockGroupAsync(groupId);

        if (TryGetLeaderSession(groupId, out var leaderId, out var leaderSession))
        {
            return await FetchHintableItemsViaSessionAsync(groupId, slot, leaderSession, leaderId);
        }

        var result = await RunAsSlotAsync<IReadOnlyList<string>>(
            group, slot,
            session => FetchHintableItemsViaSessionAsync(groupId, slot, session, slot.Id),
            keepAlive: true);

        return result ?? Array.Empty<string>();
    }

    /// <summary><paramref name="session"/> need not be <paramref name="slot"/>'s own; <paramref name="roomInfoOwnerSlotId"/> is the session's slot.</summary>
    private async Task<IReadOnlyList<string>> FetchHintableItemsViaSessionAsync(
        Guid groupId, SlotProfile slot, IArchipelagoSession session, Guid roomInfoOwnerSlotId)
    {
        var ownGame = session.Players.AllPlayers
            .FirstOrDefault(p => string.Equals(p.Name, slot.SlotName, StringComparison.OrdinalIgnoreCase))?.Game;
        if (string.IsNullOrEmpty(ownGame))
        {
            return Array.Empty<string>();
        }

        var currentChecksum = GetDataPackageChecksum(roomInfoOwnerSlotId, ownGame);
        var cached = _persistenceService?.LoadDataPackageCache(groupId, ownGame);
        if (cached is not null && currentChecksum is not null &&
            string.Equals(cached.Checksum, currentChecksum, StringComparison.Ordinal))
        {
            return cached.ItemNames;
        }

        var gameData = await FetchGameDataFromDataPackageAsync(session, ownGame, _dataPackageRequestTimeout);
        if (gameData is null)
        {
            return Array.Empty<string>();
        }

        SaveGameDataCache(groupId, ownGame, currentChecksum, gameData);
        return gameData.ItemNames;
    }

    public async Task<GameDataNames?> GetGameDataAsync(GroupViewModel group, string game)
    {
        var groupId = group.Group.Id;
        if (string.IsNullOrEmpty(game) || !TryGetLeaderSession(groupId, out var leaderId, out var leaderSession))
        {
            return null;
        }

        var currentChecksum = GetDataPackageChecksum(leaderId, game);

        // A cache entry from before LocationIds existed counts as a miss.
        var cached = _persistenceService?.LoadDataPackageCache(groupId, game);
        if (cached?.LocationIds is not null && currentChecksum is not null &&
            string.Equals(cached.Checksum, currentChecksum, StringComparison.Ordinal))
        {
            return new GameDataNames { ItemNames = cached.ItemNames, LocationIds = cached.LocationIds };
        }

        var gameData = await FetchGameDataFromDataPackageAsync(leaderSession, game, _dataPackageRequestTimeout);
        if (gameData is not null)
        {
            SaveGameDataCache(groupId, game, currentChecksum, gameData);
        }

        return gameData;
    }

    private string? GetDataPackageChecksum(Guid slotId, string game) =>
        _roomInfoBySlot.TryGetValue(slotId, out var roomInfo) &&
        roomInfo.DataPackageChecksums is { } checksums &&
        checksums.TryGetValue(game, out var checksum)
            ? checksum
            : null;

    public RoomSettingsSnapshot? GetRoomSettings(GroupViewModel group)
    {
        if (!TryGetLeaderSession(group.Group.Id, out _, out var session) ||
            session.RoomState is not { } room)
        {
            return null;
        }

        return new RoomSettingsSnapshot
        {
            ReleaseMode = PermissionToOptionValue(room.ReleasePermissions),
            CollectMode = PermissionToOptionValue(room.CollectPermissions),
            RemainingMode = PermissionToOptionValue(room.RemainingPermissions),
            HintCostPercentage = room.HintCostPercentage,
            LocationCheckPoints = room.LocationCheckPoints,
            HasPassword = room.HasPassword,
        };
    }

    /// <summary>The exact value strings MultiServer.py's "/option ..._mode" accepts.</summary>
    internal static string PermissionToOptionValue(Permissions permissions) => permissions switch
    {
        Permissions.AutoEnabled => "auto_enabled",
        Permissions.Auto => "auto",
        Permissions.Goal => "goal",
        Permissions.Enabled => "enabled",
        _ => "disabled",
    };

    /// <summary>Without a checksum to validate against, a cache entry would always count as stale.</summary>
    private void SaveGameDataCache(Guid groupId, string game, string? checksum, GameDataNames gameData)
    {
        if (gameData.ItemNames.Count > 0 && checksum is not null)
        {
            _persistenceService?.SaveDataPackageCache(groupId, game, new DataPackageCacheEntry
            {
                Checksum = checksum,
                ItemNames = gameData.ItemNames.ToList(),
                LocationIds = gameData.LocationIds.ToDictionary(p => p.Key, p => p.Value),
            });
        }
    }

    /// <summary>See Umsetzungsplan.md, "DataPackage-Abruf per Rohpaket".</summary>
    private static async Task<GameDataNames?> FetchGameDataFromDataPackageAsync(IArchipelagoSession session, string game, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<GameDataNames?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPacketReceived(ArchipelagoPacketBase packet)
        {
            if (packet is DataPackagePacket dataPackagePacket)
            {
                tcs.TrySetResult(dataPackagePacket.DataPackage.Games.TryGetValue(game, out var gameData)
                    ? new GameDataNames
                    {
                        ItemNames = gameData.ItemLookup.Keys.ToList(),
                        LocationIds = new Dictionary<string, long>(gameData.LocationLookup),
                    }
                    : null);
            }
        }

        void OnSocketClosed(string reason) => tcs.TrySetResult(null);

        session.Socket.PacketReceived += OnPacketReceived;
        session.Socket.SocketClosed += OnSocketClosed;
        try
        {
            session.Socket.SendPacket(new GetDataPackagePacket { Games = new[] { game } });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            return completed == tcs.Task ? await tcs.Task : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            session.Socket.PacketReceived -= OnPacketReceived;
            session.Socket.SocketClosed -= OnSocketClosed;
        }
    }

    /// <summary>
    /// Caller holds the group's gate. See Umsetzungsplan.md, "RunAsSlotAsync / ReleaseHeldSessionAsync (Hint-Picker)".
    /// </summary>
    /// <param name="keepAlive">Leave a newly opened session for <see cref="ReleaseHeldSessionAsync"/> to close.</param>
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
            return await action(session);
        }

        try
        {
            return await action(session);
        }
        finally
        {
            await CloseTemporarySessionAsync(group, slot, session);
        }
    }

    private Task RunAsSlotAsync(GroupViewModel group, SlotProfile slot, Action<IArchipelagoSession> action, bool keepAlive = false) =>
        RunAsSlotAsync<object?>(group, slot, session =>
        {
            action(session);
            return Task.FromResult<object?>(null);
        }, keepAlive);

    /// <summary>Closes a kept-alive Hint-picker session; never the leader's.</summary>
    public async Task ReleaseHeldSessionAsync(GroupViewModel group, SlotProfile slot)
    {
        var groupId = group.Group.Id;
        using var groupLock = await LockGroupAsync(groupId);

        if (IsLeader(groupId, slot.Id))
        {
            return;
        }

        if (_sessions.TryRemove(slot.Id, out var session))
        {
            await _socketCleanup.CloseAsync(group, slot, session);
        }
    }

    /// <summary>See Umsetzungsplan.md, "Passwort-Prompt und Retry-Flow".</summary>
    private static bool NeedsPasswordPrompt(GroupViewModel group, SlotProfile slot) =>
        slot.RequiresPassword && string.IsNullOrEmpty(slot.Password) && string.IsNullOrEmpty(group.Group.Password);

    /// <summary>Persisted so the belief survives a restart.</summary>
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
    /// Null (failure already reported) if connect or login fails. See Umsetzungsplan.md,
    /// "Transiente Verbindungsfehler und Retry" and "Passwort-Prompt und Retry-Flow".
    /// </summary>
    /// <param name="isBackgroundCatchUp">Waits behind every interactive connect at the app-wide <see cref="ConnectGate"/>.</param>
    private async Task<IArchipelagoSession?> ConnectSlotSessionAsync(GroupViewModel group, SlotProfile slot, bool isLeaderSession, CancellationToken cancellationToken = default, bool isBackgroundCatchUp = false)
    {
        for (var attempt = 1; attempt <= 1 + MaxTransientConnectRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var session = TryCreateSession(group, slot, isLeaderSession);
            if (session is null)
            {
                return null;
            }

            // Fresh per attempt so a retry's event handlers see its own flag.
            var backlog = new BacklogState();
            SubscribeSessionEvents(group, slot, session, isLeaderSession, backlog);

            LoginOutcome outcome;
            try
            {
                outcome = await ConnectAndLoginAsync(group, slot, session, isLeaderSession, isBackgroundCatchUp, cancellationToken);
            }
            catch (Exception ex) when (IsTransientConnectFailure(ex) && attempt <= MaxTransientConnectRetries)
            {
                var keepRetrying = await DiscardForTransientRetryAsync(group, slot, session, ex, attempt, cancellationToken);
                if (!keepRetrying)
                {
                    return null;
                }

                continue;
            }
            catch (Exception ex)
            {
                ReportFailure(group, isLeaderSession, $"Connection failed for {slot.DisplayName}: {ex.Message}");
                _diagnosticLogger.Error($"[{group.Group.Name}] Connection failed for {slot.DisplayName}", ex);
                await _socketCleanup.CloseAsync(group, slot, session);
                return null;
            }

            switch (outcome)
            {
                case LoginOutcome.RetryWithNewPassword:
                    attempt--; // a password retry doesn't consume a MaxTransientConnectRetries slot.
                    continue;
                case LoginOutcome.Abandoned:
                    return null;
            }

            ActivateConnectedSession(group, slot, session, isLeaderSession, backlog);
            return session;
        }

        // Unreachable: the last attempt either succeeds or hits the plain catch above.
        return null;
    }

    private IArchipelagoSession? TryCreateSession(GroupViewModel group, SlotProfile slot, bool isLeaderSession)
    {
        try
        {
            return _sessionFactory.CreateSession(group.Group.Host, group.Group.Port);
        }
        catch (Exception ex)
        {
            ReportFailure(group, isLeaderSession, $"Could not create session for {slot.DisplayName}: {ex.Message}");
            _diagnosticLogger.Error($"[{group.Group.Name}] Could not create session for {slot.DisplayName}", ex);
            return null;
        }
    }

    /// <summary>Subscribed before the socket opens so nothing sent right after login is missed.</summary>
    private void SubscribeSessionEvents(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, bool isLeaderSession, BacklogState backlog)
    {
        session.Socket.SocketClosed += reason => OnSocketClosed(group, slot, reason);
        session.Socket.ErrorReceived += (ex, message) => OnSocketError(group, slot, ex, message);

        if (isLeaderSession)
        {
            // Only the leader stays open long enough for these to matter.
            session.MessageLog.OnMessageReceived += logMessage => _sessionEvents.OnLeaderMessageReceived(group, session, slot, logMessage);
            session.MessageLog.OnMessageReceived += logMessage => ForwardPossibleServerReply(group, logMessage);
        }

        // The server re-delivers the full item history on every connect.
        _messageHistoryService.ClearReceivedItemsForSlot(group, slot.Id);

        session.Items.ItemReceived += helper =>
            _sessionEvents.OnItemReceived(group, slot, session, helper, isLeaderSession, backlog.HasAnnouncedConnection);
    }

    private void OnSocketError(GroupViewModel group, SlotProfile slot, Exception ex, string message)
    {
        // Not real errors. See Umsetzungsplan.md, "Erwartete Exceptions durch die eigenen Workarounds".
        if (SocketCleanup.IsExpectedSendQueueCompletionError(ex))
        {
            return;
        }

        if (SocketCleanup.IsMalformedBounceDataError(ex))
        {
            _diagnosticLogger.Warning($"[{group.Group.Name}] [{slot.DisplayName}] Rejected malformed Bounce packet: {ex.Message}");
            return;
        }

        _messageHistoryService.HandleError(group, $"[{slot.DisplayName}] {message}");
        _diagnosticLogger.Warning($"[{group.Group.Name}] [{slot.DisplayName}] {message}");
    }

    /// <summary>See Umsetzungsplan.md, "Admin-Ansicht: ServerReplyReceived und LeaderConnected".</summary>
    private void ForwardPossibleServerReply(GroupViewModel group, LogMessage logMessage)
    {
        if (SessionEventTranslator.ClassifyServerReply(logMessage) is { } kind)
        {
            var text = AdminEventFormatter.MaskPasswords(logMessage.ToString());
            Dispatcher.UIThread.Post(() => ServerReplyReceived?.Invoke(group, kind, text));
        }
    }

    private enum LoginOutcome
    {
        Succeeded,

        /// <summary>Already reported and cleaned up.</summary>
        Abandoned,

        RetryWithNewPassword,
    }

    /// <summary>May throw a transient error. See Umsetzungsplan.md, "Passwort-Prompt und Retry-Flow".</summary>
    private async Task<LoginOutcome> ConnectAndLoginAsync(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, bool isLeaderSession, bool isBackgroundCatchUp, CancellationToken cancellationToken)
    {
        if (NeedsPasswordPrompt(group, slot) && PasswordRequested is not null)
        {
            var obtained = await PasswordRequested(group, slot, false /* isRetryAfterFailure */, cancellationToken);
            if (!obtained || cancellationToken.IsCancellationRequested)
            {
                return QuietlyAbandon(group, isLeaderSession);
            }
        }

        // slot.Password is only set for a custom-hosted room's per-slot override.
        var effectivePassword = !string.IsNullOrEmpty(slot.Password) ? slot.Password : group.Group.Password;

        var loginResult = await OpenSocketAndLoginAsync(group, slot, session, effectivePassword, isBackgroundCatchUp, cancellationToken);
        if (loginResult is null)
        {
            return LoginOutcome.Abandoned;
        }

        if (loginResult is not LoginSuccessful)
        {
            return await HandleLoginFailureAsync(group, slot, session, isLeaderSession, (LoginFailure)loginResult, cancellationToken);
        }

        UpdateRequiresPassword(group, slot, !string.IsNullOrEmpty(effectivePassword));
        return LoginOutcome.Succeeded;
    }

    /// <summary>
    /// Holds the app-wide <see cref="ConnectGate"/> only for the handshake itself - never across a password
    /// prompt, so an open dialog doesn't stall every other server. Null = cancelled, already cleaned up.
    /// </summary>
    private async Task<LoginResult?> OpenSocketAndLoginAsync(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, string? effectivePassword, bool isBackgroundCatchUp, CancellationToken cancellationToken)
    {
        using var gateLease = await _connectGate.EnterAsync(isBackgroundCatchUp, cancellationToken);
        if (gateLease is null)
        {
            return null; // socket never opened
        }

        var roomInfo = await session.ConnectAsync();
        if (roomInfo is not null)
        {
            _roomInfoBySlot[slot.Id] = roomInfo;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await _socketCleanup.CloseAsync(group, slot, session);
            return null;
        }

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

        return loginResult;
    }

    /// <summary>Only InvalidPassword is retried. See Umsetzungsplan.md, "Passwort-Prompt und Retry-Flow".</summary>
    private async Task<LoginOutcome> HandleLoginFailureAsync(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, bool isLeaderSession, LoginFailure failure, CancellationToken cancellationToken)
    {
        if (failure.ErrorCodes.Contains(ConnectionRefusedError.InvalidPassword))
        {
            UpdateRequiresPassword(group, slot, true);

            if (PasswordRequested is not null)
            {
                await _socketCleanup.CloseAsync(group, slot, session);

                var provided = await PasswordRequested(group, slot, true /* isRetryAfterFailure */, cancellationToken);
                return provided && !cancellationToken.IsCancellationRequested
                    ? LoginOutcome.RetryWithNewPassword
                    : QuietlyAbandon(group, isLeaderSession);
            }
        }

        var errorText = string.Join("; ", failure.Errors);
        ReportFailure(group, isLeaderSession, $"Login failed for {slot.DisplayName}: {errorText}");
        _diagnosticLogger.Warning($"[{group.Group.Name}] Login rejected for {slot.DisplayName}: {errorText}");
        await _socketCleanup.CloseAsync(group, slot, session);
        return LoginOutcome.Abandoned;
    }

    /// <summary>A declined/cancelled password prompt is a quiet skip, not an error.</summary>
    private static LoginOutcome QuietlyAbandon(GroupViewModel group, bool isLeaderSession)
    {
        if (isLeaderSession)
        {
            SetConnectionState(group, ConnectionState.Disconnected);
        }

        return LoginOutcome.Abandoned;
    }

    /// <summary>False if cancelled meanwhile. See Umsetzungsplan.md, "Transiente Verbindungsfehler und Retry".</summary>
    private async Task<bool> DiscardForTransientRetryAsync(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, Exception ex, int attempt, CancellationToken cancellationToken)
    {
        await _socketCleanup.CloseAsync(group, slot, session);
        _messageHistoryService.HandleError(
            group,
            $"Connecting {slot.DisplayName} hit a transient error ({ex.GetType().Name}), retrying ({attempt}/{MaxTransientConnectRetries})...");
        _diagnosticLogger.Warning($"[{group.Group.Name}] Transient connect error for {slot.DisplayName} ({ex.GetType().Name}), retrying ({attempt}/{MaxTransientConnectRetries})");

        try
        {
            await Task.Delay(_transientConnectRetryDelay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void ReportFailure(GroupViewModel group, bool isLeaderSession, string message)
    {
        if (isLeaderSession)
        {
            SetConnectionState(group, ConnectionState.Error);
        }

        _messageHistoryService.HandleError(group, message);
    }

    private void ActivateConnectedSession(GroupViewModel group, SlotProfile slot, IArchipelagoSession session, bool isLeaderSession, BacklogState backlog)
    {
        _sessions[slot.Id] = session;

        // Populated on every login, kept live only for the leader.
        SessionEventTranslator.UpdateLocationProgress(slot, session);
        _sessionEvents.UpdateMissingLocationsCache(slot, session);

        if (isLeaderSession)
        {
            session.Locations.CheckedLocationsUpdated += _ =>
            {
                SessionEventTranslator.UpdateLocationProgress(slot, session);
                _sessionEvents.UpdateMissingLocationsCache(slot, session);
            };

            EnableDeathLink(group, session);
        }

        // See Umsetzungsplan.md, "Warum die Leader-Session pro Sibling-Slot ein eigenes TrackHints braucht".
        void OnHintsUpdated(Hint[] hints) =>
            _sessionEvents.OnHintsUpdated(group, session, hints, isLeaderSession && backlog.HasAnnouncedConnection);

        session.Hints.TrackHints(OnHintsUpdated, retrieveCurrentlyUnlockedHints: true);

        if (isLeaderSession)
        {
            TrackSiblingHintsOnLeader(group, slot, session, OnHintsUpdated);
            _messageHistoryService.HandleConnected(group, slot);
            _diagnosticLogger.Info($"[{group.Group.Name}] Connected as leader: {slot.DisplayName}");
        }

        // See Umsetzungsplan.md, "Warum der Item-Backlog eine Grace-Period braucht".
        _ = Task.Delay(_itemBacklogGracePeriod)
                .ContinueWith(_ => backlog.HasAnnouncedConnection = true, TaskScheduler.Default);
    }

    /// <summary>Only the leader gets DeathLink. See Umsetzungsplan.md, "DeathLink".</summary>
    private void EnableDeathLink(GroupViewModel group, IArchipelagoSession session)
    {
        var deathLinkService = _sessionFactory.CreateDeathLinkService(session);
        if (deathLinkService is null)
        {
            return;
        }

        deathLinkService.EnableDeathLink();
        deathLinkService.OnDeathLinkReceived += deathLink =>
        {
            _messageHistoryService.HandleDeathLinkReceived(group, deathLink);
            _attentionTracker?.Report(group, AttentionCategory.DeathLink);
        };
    }

    private static void TrackSiblingHintsOnLeader(GroupViewModel group, SlotProfile leaderSlot, IArchipelagoSession session, Action<Hint[]> onHintsUpdated)
    {
        var roster = SessionEventTranslator.BuildSlotRoster(session, group.Group);
        foreach (var (numericSlotId, siblingSlot) in roster)
        {
            if (siblingSlot.Id != leaderSlot.Id)
            {
                session.Hints.TrackHints(onHintsUpdated, retrieveCurrentlyUnlockedHints: true, slot: numericSlotId);
            }
        }
    }

    /// <summary>A class so event handlers and the grace-period continuation share one flag per attempt.</summary>
    private sealed class BacklogState
    {
        public volatile bool HasAnnouncedConnection;
    }

    /// <summary>See Umsetzungsplan.md, "Transiente Verbindungsfehler und Retry".</summary>
    // internal rather than private so AvaloniaApplication1.Tests can exercise this.
    internal static bool IsTransientConnectFailure(Exception ex) =>
        ex is TaskCanceledException or OperationCanceledException or TimeoutException;

    private void OnSocketClosed(GroupViewModel group, SlotProfile slot, string reason)
    {
        var wasConnected = _sessions.TryRemove(slot.Id, out _);
        if (!wasConnected)
        {
            // Handshake still pending, or a deliberate close that did its own bookkeeping.
            return;
        }

        var groupId = group.Group.Id;
        if (!IsLeader(groupId, slot.Id))
        {
            // A catch-up session closing before its caller's teardown ran.
            return;
        }

        _messageHistoryService.HandleDisconnected(group, slot, reason);
        _leaderSlotByGroup.TryRemove(groupId, out _);
        PostLeaderState(group, null, null);

        // No leader left to piggyback on; the next successful connect re-queues every slot.
        _catchUpQueue.RemoveGroup(groupId);

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
    /// Reconnects as the slot that dropped, not PreferredLeaderSlotId. See Umsetzungsplan.md,
    /// "Reconnect-Scheduling nach unerwartetem Drop".
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
            // Cancelled by a manual switch/disconnect.
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

    private Guid? GetLeaderId(Guid groupId) =>
        _leaderSlotByGroup.TryGetValue(groupId, out var leaderId) ? leaderId : null;

    private bool IsLeader(Guid groupId, Guid slotId) => GetLeaderId(groupId) == slotId;

    private bool TryGetLeaderSession(Guid groupId, out Guid leaderId, [NotNullWhen(true)] out IArchipelagoSession? session)
    {
        session = null;
        return _leaderSlotByGroup.TryGetValue(groupId, out leaderId) && _sessions.TryGetValue(leaderId, out session);
    }

    /// <summary>Not reentrant; dispose to release.</summary>
    private async Task<IDisposable> LockGroupAsync(Guid groupId)
    {
        var gate = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        return new GroupLockRelease(gate);
    }

    private sealed class GroupLockRelease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    /// <summary>Cancellable by a manual disconnect via <see cref="_connectCancellationSources"/>.</summary>
    private async Task<IArchipelagoSession?> ConnectCancellableAsync(GroupViewModel group, SlotProfile slot, bool isLeaderSession, bool isBackgroundCatchUp = false)
    {
        var groupId = group.Group.Id;
        using var connectCts = new CancellationTokenSource();
        _connectCancellationSources[groupId] = connectCts;
        try
        {
            return await ConnectSlotSessionAsync(group, slot, isLeaderSession, connectCts.Token, isBackgroundCatchUp);
        }
        finally
        {
            _connectCancellationSources.TryRemove(groupId, out _);
        }
    }

    /// <summary>No-op if something else already replaced or removed the session.</summary>
    private async Task CloseTemporarySessionAsync(GroupViewModel group, SlotProfile slot, IArchipelagoSession session)
    {
        if (_sessions.TryRemove(slot.Id, out var stillTracked) && stillTracked == session)
        {
            await _socketCleanup.CloseAsync(group, slot, session);
        }
    }

    private static void PostLeaderState(GroupViewModel group, Guid? leaderSlotId, SlotProfile? leaderSlot) =>
        Dispatcher.UIThread.Post(() => group.SetLeaderStateWithoutTriggeringSwitch(leaderSlotId, leaderSlot));

    private static void SetConnectionState(GroupViewModel group, ConnectionState state) =>
        Dispatcher.UIThread.Post(() => group.ConnectionState = state);
}
