using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
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

    private readonly IMessageHistoryService _messageHistoryService;
    private readonly IHintService _hintService;
    private readonly ConcurrentDictionary<Guid, ArchipelagoSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reconnectTokens = new();

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

    public async Task ConnectAsync(TabViewModel tab)
    {
        var profile = tab.ServerProfile;

        if (_sessions.ContainsKey(profile.Id))
        {
            return;
        }

        // A manual (re-)connect attempt always takes over from any pending
        // auto-reconnect loop for this profile, and lifts a previous manual
        // disconnect's suppression so AutoConnect can take effect again on
        // the next unexpected drop.
        CancelPendingReconnect(profile.Id);
        _autoReconnectSuppressed.TryRemove(profile.Id, out _);

        SetConnectionState(tab, ConnectionState.Connecting);

        ArchipelagoSession session;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(profile.Host, profile.Port);
        }
        catch (Exception ex)
        {
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
            EventSegmentBuilder.BuildChatSegments(logMessage, GetOtherConnectedSlotIds(profile)));
        session.Items.ItemReceived += helper => OnItemReceived(tab, profile, helper);

        try
        {
            await session.ConnectAsync();

            var loginResult = await session.LoginAsync(
                string.Empty, // generic tracker client: no specific game implementation
                profile.SlotName,
                ItemsHandlingFlags.AllItems,
                tags: new[] { "Tracker" },
                password: string.IsNullOrEmpty(profile.Password) ? null : profile.Password);

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

    public Task DisconnectAsync(TabViewModel tab)
    {
        var profileId = tab.ServerProfile.Id;
        CancelPendingReconnect(profileId);

        // Stays set (see field comment) until the user explicitly reconnects,
        // so AutoConnect cannot bring this profile back by itself.
        _autoReconnectSuppressed[profileId] = true;

        if (_sessions.TryRemove(profileId, out var session))
        {
            _sessionProfiles.TryRemove(profileId, out _);
            // SocketClosed will fire and move the tab to Disconnected/log the event.
            session.Socket.DisconnectAsync();
        }
        else
        {
            SetConnectionState(tab, ConnectionState.Disconnected);
        }

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
        _sessions.TryRemove(profileId, out _);
        _sessionProfiles.TryRemove(profileId, out _);

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
