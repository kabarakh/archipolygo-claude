using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// Owns one <see cref="ArchipelagoSession"/> per connected profile and wires its
/// events to <see cref="IMessageHistoryService"/> and <see cref="IHintService"/>.
/// Built to hold several concurrent sessions (see Phase 3). Also handles sending
/// chat messages (Phase 5) and auto-reconnecting profiles whose
/// <see cref="ServerProfile.AutoConnect"/> is set after an unexpected disconnect.
/// </summary>
public class ConnectionManager : IConnectionManager
{
    // Backoff schedule for auto-reconnect attempts; the last value repeats for further attempts.
    private static readonly int[] ReconnectDelaysSeconds = { 5, 10, 30 };

    // Minimum time between the *start* of two consecutive connection
    // attempts (across all tabs), so opening the app with several
    // AutoConnect tabs - or several tabs auto-reconnecting at once - doesn't
    // hit the Archipelago server with a burst of simultaneous handshakes.
    // See EnqueueConnect/ProcessConnectQueueAsync.
    private static readonly TimeSpan ConnectAttemptSpacing = TimeSpan.FromSeconds(3);

    // The Archipelago network-protocol version this client implements (used
    // in the login handshake) - not this app's own version number.
    private static readonly Version ArchipelagoProtocolVersion = new(0, 6, 7);

    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;
    private readonly ConcurrentDictionary<Guid, ArchipelagoSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reconnectTokens = new();

    // Serializes connection attempts across all tabs with ConnectAttemptSpacing
    // between the start of one and the start of the next. Guarded by _queueLock.
    private readonly List<TabViewModel> _connectQueue = new();
    private readonly object _queueLock = new();
    private bool _queueRunning;

    // Marks a profile whose queued connect attempt should be skipped without
    // ever starting a handshake, because the user disconnected the tab while
    // it was still waiting its turn (see DisconnectAsync). Cleared again at
    // the start of the next ConnectAsync call for that profile.
    private readonly ConcurrentDictionary<Guid, bool> _queueSkip = new();

    // Present for a profile only while ConnectNowAsync's handshake
    // (ConnectAsync/LoginAsync) is actually in flight for it. Lets
    // DisconnectAsync interrupt that wait immediately so the rest of the
    // connect queue isn't blocked behind a connection nobody wants anymore -
    // the next queued slot is "brought to the front" right away instead of
    // waiting for this attempt to time out on its own.
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _cancellationSignals = new();

    // Mirrors _sessions' keys while connected; used to find other tabs' own
    // slot ids on the same Archipelago server instance (same host+port), so
    // incoming chat messages can color "also tracked by this app" names
    // differently from other multiworld players (see EventSegmentBuilder).
    private readonly ConcurrentDictionary<Guid, ServerProfile> _sessionProfiles = new();

    // Set right before a deliberate DisconnectAsync and only cleared again by a
    // subsequent explicit ConnectAsync (Connect button, duplicate, startup
    // auto-connect, ...). Deliberately NOT consumed/removed by OnSocketClosed:
    // some socket implementations fire SocketClosed more than once for a single
    // deliberate close (e.g. a graceful-close event followed by the underlying
    // transport actually dropping), and a one-shot flag would only suppress the
    // first of those, letting AutoConnect reconnect on the second. Used as a set
    // (the bool value is always true); presence means "stay disconnected".
    private readonly ConcurrentDictionary<Guid, bool> _autoReconnectSuppressed = new();

    public ConnectionManager(IMessageHistoryService messageHistoryService, IHintService hintService)
    {
        _messageHistoryService = messageHistoryService;
        _hintService = hintService;
    }

    public Task ConnectAsync(TabViewModel tab)
    {
        var profile = tab.ServerProfile;

        if (_sessions.ContainsKey(profile.Id))
        {
            return Task.CompletedTask;
        }

        lock (_queueLock)
        {
            // Already waiting in the queue or actively connecting - nothing more to do.
            if (_connectQueue.Contains(tab) || _cancellationSignals.ContainsKey(profile.Id))
            {
                return Task.CompletedTask;
            }
        }

        // A manual (re-)connect attempt always takes over from any pending
        // auto-reconnect loop for this profile, and lifts a previous manual
        // disconnect's suppression so AutoConnect can take effect again on
        // the next unexpected drop.
        CancelPendingReconnect(profile.Id);
        _autoReconnectSuppressed.TryRemove(profile.Id, out _);
        _queueSkip.TryRemove(profile.Id, out _);

        EnqueueConnect(tab);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds <paramref name="tab"/> to the connect queue (see
    /// <see cref="ConnectAttemptSpacing"/>) and starts
    /// <see cref="ProcessConnectQueueAsync"/> if it isn't already running.
    /// </summary>
    private void EnqueueConnect(TabViewModel tab)
    {
        SetConnectionState(tab, ConnectionState.Queued);

        lock (_queueLock)
        {
            _connectQueue.Add(tab);

            if (_queueRunning)
            {
                return;
            }

            _queueRunning = true;
        }

        _ = ProcessConnectQueueAsync();
    }

    /// <summary>
    /// Processes the connect queue one tab at a time, waiting
    /// <see cref="ConnectAttemptSpacing"/> between the end of one attempt and
    /// the start of the next (but never after the last queued attempt).
    /// </summary>
    private async Task ProcessConnectQueueAsync()
    {
        while (true)
        {
            TabViewModel next;
            lock (_queueLock)
            {
                if (_connectQueue.Count == 0)
                {
                    _queueRunning = false;
                    return;
                }

                next = _connectQueue[0];
                _connectQueue.RemoveAt(0);
            }

            // Disconnected while still waiting its turn - skip it entirely;
            // no connection was attempted, so no spacing delay is charged
            // for it either, and the next queued slot is reached right away.
            if (_queueSkip.TryRemove(next.ServerProfile.Id, out _))
            {
                continue;
            }

            await ConnectNowAsync(next);

            bool moreQueued;
            lock (_queueLock)
            {
                moreQueued = _connectQueue.Count > 0;
            }

            if (moreQueued)
            {
                await Task.Delay(ConnectAttemptSpacing);
            }
        }
    }

    /// <summary>
    /// Performs the actual connection attempt for <paramref name="tab"/>:
    /// opens the websocket and logs in. Only ever called from
    /// <see cref="ProcessConnectQueueAsync"/>, one tab at a time.
    /// </summary>
    private async Task ConnectNowAsync(TabViewModel tab)
    {
        var profile = tab.ServerProfile;

        // Registered for the duration of the handshake so DisconnectAsync can
        // interrupt the await below without waiting for the server to
        // actually respond (see field comment on _cancellationSignals).
        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cancellationSignals[profile.Id] = cancelTcs;

        SetConnectionState(tab, ConnectionState.Connecting);

        ArchipelagoSession session;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(profile.Host, profile.Port);
        }
        catch (Exception ex)
        {
            _cancellationSignals.TryRemove(profile.Id, out _);
            SetConnectionState(tab, ConnectionState.Error);
            _messageHistoryService.HandleError(tab, $"Could not create session: {ex.Message}");
            return;
        }

        // Subscriptions must happen before connecting so that no early items/messages are missed.
        session.Socket.SocketClosed += reason => OnSocketClosed(tab, profile.Id, reason);
        session.Socket.ErrorReceived += (_, message) => _messageHistoryService.HandleError(tab, message);
        session.MessageLog.OnMessageReceived += logMessage => _messageHistoryService.HandleChatMessage(
            tab,
            logMessage.ToString(),
            EventSegmentBuilder.BuildChatSegments(logMessage, GetOtherConnectedSlotIds(profile)),
            // "X sent Y to Z" (and the cheat-console/hint-completion variants,
            // which derive from the same base class) describe an item
            // changing hands, not something someone typed - file those under
            // the "Item" log filter rather than "Chat".
            logMessage is ItemSendLogMessage ? EventType.ItemReceived : EventType.Chat);
        session.Items.ItemReceived += helper => OnItemReceived(tab, profile, helper);

        try
        {
            var connectTask = session.ConnectAsync();
            if (await Task.WhenAny(connectTask, cancelTcs.Task) == cancelTcs.Task)
            {
                // Disconnected while still waiting for the server's RoomInfo -
                // don't keep the queue waiting on it; drop the socket once the
                // call does eventually return (or right away if it already has).
                _ = connectTask.ContinueWith(_ => TryCloseSocket(session), TaskScheduler.Default);
                return;
            }

            await connectTask; // surfaces a connect-time exception, if any

            var loginTask = session.LoginAsync(
                string.Empty, // generic tracker client: no specific game implementation
                profile.SlotName,
                ItemsHandlingFlags.AllItems,
                ArchipelagoProtocolVersion,
                tags: new[] { "Tracker", "AP", "Poly" },
                password: string.IsNullOrEmpty(profile.Password) ? null : profile.Password);

            if (await Task.WhenAny(loginTask, cancelTcs.Task) == cancelTcs.Task)
            {
                _ = loginTask.ContinueWith(_ => TryCloseSocket(session), TaskScheduler.Default);
                return;
            }

            var loginResult = await loginTask;

            if (loginResult is not LoginSuccessful)
            {
                var failure = (LoginFailure)loginResult;
                var errorText = string.Join("; ", failure.Errors);
                SetConnectionState(tab, ConnectionState.Error);
                _messageHistoryService.HandleError(tab, $"Login failed: {errorText}");
                return;
            }
        }
        catch (Exception ex)
        {
            SetConnectionState(tab, ConnectionState.Error);
            _messageHistoryService.HandleError(tab, $"Connection failed: {ex.Message}");
            return;
        }
        finally
        {
            _cancellationSignals.TryRemove(profile.Id, out _);
        }

        _sessions[profile.Id] = session;
        _sessionProfiles[profile.Id] = profile;

        // Registered after a successful login: fires immediately with the
        // currently unlocked hints, then again on every later change.
        session.Hints.TrackHints(
            hints => OnHintsUpdated(tab, profile, session, hints),
            retrieveCurrentlyUnlockedHints: true);

        SetConnectionState(tab, ConnectionState.Connected);
        _messageHistoryService.HandleConnected(tab);
    }

    private static void TryCloseSocket(ArchipelagoSession session)
    {
        try
        {
            session.Socket.DisconnectAsync();
        }
        catch
        {
            // Best-effort cleanup of an attempt that was already cancelled; nothing more to do.
        }
    }

    public Task DisconnectAsync(TabViewModel tab)
    {
        var profileId = tab.ServerProfile.Id;
        CancelPendingReconnect(profileId);

        // Stays set (see field comment) until the user explicitly reconnects,
        // so AutoConnect cannot bring this profile back by itself.
        _autoReconnectSuppressed[profileId] = true;

        // No-op unless this tab is actually still sitting in the connect
        // queue when ProcessConnectQueueAsync reaches it; cleared again at
        // the start of the next ConnectAsync call either way.
        _queueSkip[profileId] = true;

        if (_cancellationSignals.TryGetValue(profileId, out var cancelTcs))
        {
            // A handshake is currently in flight for this slot - stop the
            // queue from waiting on it; see field comment on _cancellationSignals.
            cancelTcs.TrySetResult(true);
        }

        if (_sessions.TryRemove(profileId, out var session))
        {
            _sessionProfiles.TryRemove(profileId, out _);
            // SocketClosed will fire and move the tab to Disconnected/log the event.
            session.Socket.DisconnectAsync();
            return Task.CompletedTask;
        }

        SetConnectionState(tab, ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(TabViewModel tab, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && _sessions.TryGetValue(tab.ServerProfile.Id, out var session))
        {
            session.Say(text);
        }

        return Task.CompletedTask;
    }

    private void OnSocketClosed(TabViewModel tab, Guid profileId, string reason)
    {
        var wasConnected = _sessions.TryRemove(profileId, out _);
        _sessionProfiles.TryRemove(profileId, out _);

        if (!wasConnected)
        {
            // The handshake (ConnectAsync/LoginAsync) itself was still
            // pending when the socket closed - e.g. a cancelled queued
            // attempt being torn down, or the server refusing the
            // connection outright. There's no established connection to
            // report as "disconnected": a handshake failure is already
            // reported via ConnectNowAsync's own catch block, and a
            // cancellation reports nothing since the user already knows
            // they cancelled it.
            return;
        }

        _messageHistoryService.HandleDisconnected(tab, reason);

        if (!_autoReconnectSuppressed.ContainsKey(profileId) && tab.ServerProfile.AutoConnect)
        {
            SetConnectionState(tab, ConnectionState.Reconnecting);
            _ = ScheduleReconnectAsync(tab);
        }
        else
        {
            SetConnectionState(tab, ConnectionState.Disconnected);
        }
    }

    private async Task ScheduleReconnectAsync(TabViewModel tab)
    {
        var profileId = tab.ServerProfile.Id;
        var cts = new CancellationTokenSource();
        _reconnectTokens[profileId] = cts;

        try
        {
            var attempt = 0;
            while (!cts.IsCancellationRequested && tab.ServerProfile.AutoConnect && !_sessions.ContainsKey(profileId))
            {
                var delaySeconds = ReconnectDelaysSeconds[Math.Min(attempt, ReconnectDelaysSeconds.Length - 1)];
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token);

                if (cts.IsCancellationRequested)
                {
                    break;
                }

                SetConnectionState(tab, ConnectionState.Reconnecting);
                await ConnectAsync(tab);

                attempt++;
            }
        }
        catch (TaskCanceledException)
        {
            // Reconnect loop was cancelled by a manual connect/disconnect; nothing to do.
        }
        finally
        {
            _reconnectTokens.TryRemove(profileId, out _);
        }
    }

    private void CancelPendingReconnect(Guid profileId)
    {
        if (_reconnectTokens.TryRemove(profileId, out var cts))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Slot ids of other tabs that are currently connected to the same
    /// Archipelago server instance (same host+port) as <paramref name="profile"/>.
    /// Used to color those players' names differently from other multiworld
    /// participants in the chat log (see <see cref="EventSegmentBuilder"/>).
    /// </summary>
    private HashSet<int> GetOtherConnectedSlotIds(ServerProfile profile)
    {
        var slotIds = new HashSet<int>();

        foreach (var (otherProfileId, otherProfile) in _sessionProfiles)
        {
            if (otherProfileId == profile.Id)
            {
                continue;
            }

            if (!string.Equals(otherProfile.Host, profile.Host, StringComparison.OrdinalIgnoreCase) ||
                otherProfile.Port != profile.Port)
            {
                continue;
            }

            if (_sessions.TryGetValue(otherProfileId, out var otherSession))
            {
                slotIds.Add(otherSession.ConnectionInfo.Slot);
            }
        }

        return slotIds;
    }

    private void OnItemReceived(TabViewModel tab, ServerProfile profile, ReceivedItemsHelper helper)
    {
        _messageHistoryService.HandleItemsReceived(tab, profile, helper.AllItemsReceived);

        // Drain the queue as documented by the library; HandleItemsReceived already
        // read everything it needs from AllItemsReceived.
        while (helper.Any())
        {
            helper.DequeueItem();
        }
    }

    private void OnHintsUpdated(TabViewModel tab, ServerProfile profile, ArchipelagoSession session, Hint[] hints)
    {
        var ownSlot = session.ConnectionInfo.Slot;
        var otherConnectedSlotIds = GetOtherConnectedSlotIds(profile);

        var snapshots = new List<HintSnapshot>(hints.Length);

        foreach (var hint in hints)
        {
            // Items belong to the receiving player's game; locations belong to the
            // finding player's game - the generic tracker login uses an empty game,
            // so the correct game must be looked up per hinted player to resolve names.
            var receivingGame = session.Players.GetPlayerInfo(hint.ReceivingPlayer)?.Game;
            var findingGame = session.Players.GetPlayerInfo(hint.FindingPlayer)?.Game;

            snapshots.Add(new HintSnapshot
            {
                Key = $"{hint.ReceivingPlayer}:{hint.FindingPlayer}:{hint.ItemId}:{hint.LocationId}",
                ReceivingPlayer = hint.ReceivingPlayer,
                FindingPlayer = hint.FindingPlayer,
                ReceivingPlayerName = session.Players.GetPlayerAlias(hint.ReceivingPlayer),
                FindingPlayerName = session.Players.GetPlayerAlias(hint.FindingPlayer),
                ItemName = session.Items.GetItemName(hint.ItemId, receivingGame) ?? $"Item #{hint.ItemId}",
                LocationName = session.Locations.GetLocationNameFromId(hint.LocationId, findingGame) ?? $"Location #{hint.LocationId}",
                Found = hint.Found,
                ItemFlags = hint.ItemFlags,
                ReceivingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.ReceivingPlayer, ownSlot, otherConnectedSlotIds),
                FindingPlayerKind = EventSegmentBuilder.ClassifyPlayerSlot(hint.FindingPlayer, ownSlot, otherConnectedSlotIds)
            });
        }

        _hintService.SyncHints(tab, profile, snapshots);
    }

    private static void SetConnectionState(TabViewModel tab, ConnectionState state) =>
        Dispatcher.UIThread.Post(() => tab.ConnectionState = state);
}
