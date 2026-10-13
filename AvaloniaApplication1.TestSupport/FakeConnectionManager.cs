using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.TestSupport;

/// <summary>
/// No-network stand-in shared by TestHarness and the tests; "connecting" just flips view-model state.
/// Hint routing is deliberately re-modeled (not the production code) so the leader-tracks-every-sibling's-hints design can be checked.
/// </summary>
public sealed class FakeConnectionManager : IConnectionManager
{
    public event Action<GroupViewModel>? GroupPersistNeeded;
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    // Never raised, so the startup "Catching up slots" banner never shows with this fake.
    public event Action<int>? SlotSyncBatchStarting;

    // Never invoked: SwitchLeaderAsync here always succeeds without a login.
    public Func<GroupViewModel, SlotProfile, bool, CancellationToken, Task<bool>>? PasswordRequested { get; set; }

    public event Action<GroupViewModel, ServerReplyKind, string>? ServerReplyReceived;
    public event Action<GroupViewModel>? LeaderConnected;

    // Models the server's per-slot hints_{team}_{slot} keys: a hint is stored under both finder's and receiver's key,
    // and only subscribers of that key are notified (no room-wide broadcast).
    private readonly Dictionary<int, List<FakeHint>> _hintsBySlot = new();
    private readonly Dictionary<int, Action<List<FakeHint>>> _hintSubscriptions = new();
    private readonly Dictionary<Guid, int> _numericSlotIds = new();
    private int _nextNumericSlotId = 1;

    private int NumericSlotId(SlotProfile slot)
    {
        if (!_numericSlotIds.TryGetValue(slot.Id, out var id))
        {
            id = _nextNumericSlotId++;
            _numericSlotIds[slot.Id] = id;
        }

        return id;
    }

    public Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(targetSlot.Id, targetSlot);
        group.ConnectionState = ConnectionState.Connected;

        // Mirrors ConnectSlotSessionAsync's TrackHints: leader's own slot plus one per sibling slot.
        _hintSubscriptions.Clear();
        TrackHintsForSlot(group, targetSlot);
        foreach (var sibling in group.Group.Slots)
        {
            if (sibling.Id == targetSlot.Id)
            {
                continue;
            }

            TrackHintsForSlot(group, sibling);
        }

        GroupPersistNeeded?.Invoke(group);

        // A new connection is never logged in as admin - same as the real server.
        _adminLoggedInGroups.Remove(group.Group.Id);
        LeaderConnected?.Invoke(group);
        return Task.CompletedTask;
    }

    private void TrackHintsForSlot(GroupViewModel group, SlotProfile slot)
    {
        var numericId = NumericSlotId(slot);
        void OnUpdated(List<FakeHint> hints) => RouteHints(group, hints);

        _hintSubscriptions[numericId] = OnUpdated;

        // retrieveCurrentlyUnlockedHints: true - replay anything stored before the subscription.
        if (_hintsBySlot.TryGetValue(numericId, out var existing) && existing.Count > 0)
        {
            OnUpdated(existing);
        }
    }

    /// <summary>
    /// Simulates a hint created in <paramref name="finder"/>'s world. If nothing subscribes to finder's key, the hint is
    /// silently lost - the real pre-fix bug for non-leader slots.
    /// </summary>
    public void SimulateHintLocation(GroupViewModel group, SlotProfile finder, string receiverName, string itemName, string locationName, ItemFlags flags)
    {
        var finderNumericId = NumericSlotId(finder);
        var configuredReceiver = group.Group.Slots.FirstOrDefault(s => string.Equals(s.SlotName, receiverName, StringComparison.OrdinalIgnoreCase));
        var receiverNumericId = configuredReceiver is not null
            ? NumericSlotId(configuredReceiver)
            : 100_000 + Math.Abs(receiverName.GetHashCode() % 1000); // unconfigured room player - never collides with a real configured slot's small id

        var hint = new FakeHint(finder, receiverName, finderNumericId, receiverNumericId, itemName, locationName, flags);

        Notify(finderNumericId, hint);
        if (receiverNumericId != finderNumericId)
        {
            Notify(receiverNumericId, hint);
        }
    }

    private void Notify(int numericSlotId, FakeHint hint)
    {
        if (!_hintsBySlot.TryGetValue(numericSlotId, out var list))
        {
            list = new List<FakeHint>();
            _hintsBySlot[numericSlotId] = list;
        }

        list.Add(hint);

        // Nobody tracks this key: the hint stays unreported (the pre-fix bug).
        if (_hintSubscriptions.TryGetValue(numericSlotId, out var callback))
        {
            callback(list);
        }
    }

    private void RouteHints(GroupViewModel group, IReadOnlyList<FakeHint> hints)
    {
        foreach (var hint in hints)
        {
            var key = $"{hint.FinderSlot.Id}:{hint.ReceiverName}:{hint.ItemName}:{hint.LocationName}";
            if (group.Hints.Any(h => h.Key == key))
            {
                continue; // already routed by an earlier notification
            }

            var finderKind = group.LeaderSlotId == hint.FinderSlot.Id
                ? EventTextSegmentKind.OwnSlotName
                : EventTextSegmentKind.ConnectedSlotName;

            group.Hints.Add(new HintEntry
            {
                Key = key,
                SlotId = hint.FinderSlot.Id,
                ReceivingPlayer = hint.ReceiverNumericId,
                FindingPlayer = hint.FinderNumericId,
                ReceivingPlayerName = hint.ReceiverName,
                FindingPlayerName = hint.FinderSlot.SlotName,
                ItemName = hint.ItemName,
                LocationName = hint.LocationName,
                ItemFlags = hint.ItemFlags,
                ItemKind = EventSegmentBuilder.ClassifyItemFlags(hint.ItemFlags),
                ReceivingPlayerKind = EventTextSegmentKind.OtherSlotName,
                FindingPlayerKind = finderKind,
                Found = false,
            });

            group.Events.Add(new EventEntry
            {
                SlotId = hint.FinderSlot.Id,
                Type = EventType.HintReceived,
                Text = $"Hint: {hint.ItemName} ({hint.FinderSlot.SlotName} -> {hint.ReceiverName}, {hint.LocationName})",
                Segments = EventSegmentBuilder.BuildHintReceivedSegments(
                    hint.ItemName, hint.ItemFlags,
                    hint.FinderSlot.SlotName, finderKind,
                    hint.ReceiverName, EventTextSegmentKind.OtherSlotName,
                    hint.LocationName),
                ConcernsOwnSlot = true,
            });
        }
    }

    private sealed record FakeHint(SlotProfile FinderSlot, string ReceiverName, int FinderNumericId, int ReceiverNumericId, string ItemName, string LocationName, ItemFlags ItemFlags);

    public Task DisconnectGroupAsync(GroupViewModel group)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(null, null);
        group.ConnectionState = ConnectionState.Disconnected;
        GroupPersistNeeded?.Invoke(group);
        return Task.CompletedTask;
    }

    public Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot) => Task.CompletedTask;

    public List<string> SentMessages { get; } = new();

    public Task SendMessageAsync(GroupViewModel group, string text)
    {
        SentMessages.Add(text);
        if (text.StartsWith("!admin", StringComparison.OrdinalIgnoreCase))
        {
            SimulateAdminServer(group, text);
        }

        return Task.CompletedTask;
    }

    // Just enough of MultiServer.py's "!admin" behavior: login outcomes as CommandResult, "must first login" when logged out,
    // failures/"/option" as AdminCommandResult, successes only as a room-wide broadcast. Replies are raised synchronously.

    /// <summary>The fake server's server_password; null = "Remote administration is disabled".</summary>
    public string? AdminServerPassword { get; set; } = "admin";

    private readonly HashSet<Guid> _adminLoggedInGroups = new();

    public void SimulateOtherClientTakesOverAdmin(GroupViewModel group) => _adminLoggedInGroups.Remove(group.Group.Id);

    private void SimulateAdminServer(GroupViewModel group, string text)
    {
        var command = text.Substring("!admin".Length).Trim();
        var leaderName = group.Group.Slots.FirstOrDefault(s => s.Id == group.LeaderSlotId)?.SlotName ?? "?";

        // The server broadcasts every "!admin ..." line, password starred out.
        var echo = AdminEventFormatter.MaskPasswords(text);
        // Same coloring as the real SessionEventTranslator: leader = own slot,
        // other configured slots = related, everyone else = other.
        var nameKinds = RoomPlayers
            .Select(p => (p.Name, string.Equals(p.Name, leaderName, StringComparison.OrdinalIgnoreCase)
                ? EventTextSegmentKind.OwnSlotName
                : group.Group.Slots.Any(s => string.Equals(s.SlotName, p.Name, StringComparison.OrdinalIgnoreCase))
                    ? EventTextSegmentKind.ConnectedSlotName
                    : EventTextSegmentKind.OtherSlotName))
            .Append((leaderName, EventTextSegmentKind.OwnSlotName))
            .ToList();

        void AddAdminEvent(string line) =>
            group.Events.Add(new EventEntry { Type = EventType.Admin, Text = line, Segments = AdminEventFormatter.BuildSegments(line, nameKinds) });

        AddAdminEvent($"{leaderName}: {echo}");

        void Reply(ServerReplyKind kind, string reply)
        {
            reply = AdminEventFormatter.MaskPasswords(reply);
            AddAdminEvent(reply);
            ServerReplyReceived?.Invoke(group, kind, reply);
        }

        if (string.IsNullOrEmpty(AdminServerPassword))
        {
            Reply(ServerReplyKind.CommandResult, "Sorry, Remote administration is disabled");
            return;
        }

        if (command.StartsWith("login ", StringComparison.OrdinalIgnoreCase))
        {
            if (command.Substring("login ".Length) == AdminServerPassword)
            {
                _adminLoggedInGroups.Add(group.Group.Id);
                Reply(ServerReplyKind.CommandResult, "Login successful. You can now issue server side commands.");
            }
            else
            {
                Reply(ServerReplyKind.CommandResult, "Password incorrect.");
            }

            return;
        }

        if (!_adminLoggedInGroups.Contains(group.Group.Id))
        {
            Reply(ServerReplyKind.CommandResult, "You must first login using !admin login [password]");
            return;
        }

        if (command == "logout")
        {
            _adminLoggedInGroups.Remove(group.Group.Id);
            Reply(ServerReplyKind.CommandResult, "Logout successful. You can no longer issue server side commands.");
            return;
        }

        var parts = command.Split(' ', 2);
        switch (parts[0])
        {
            case "/send":
            case "/send_multiple":
                Reply(ServerReplyKind.Broadcast, $"Cheat console: sending {(parts.Length > 1 ? parts[1] : string.Empty)}");
                break;
            case "/release":
                Reply(ServerReplyKind.Broadcast, $"{(parts.Length > 1 ? parts[1] : "?")} (Team #1) has released all remaining items from their world.");
                break;
            case "/collect":
                Reply(ServerReplyKind.Broadcast, $"{(parts.Length > 1 ? parts[1] : "?")} (Team #1) has collected their items from other worlds.");
                break;
            case "/option":
                var option = parts.Length > 1 ? parts[1].Split(' ', 2) : new[] { "?" };
                if (option[0] == "server_password" && option.Length > 1)
                {
                    AdminServerPassword = option[1].Trim('"');
                }

                Reply(ServerReplyKind.AdminCommandResult, $"Set option {option[0]} to {(option.Length > 1 ? option[1].Trim('"') : string.Empty)}");
                break;
            case "/send_location":
                // Success is silent on the real server too (only the found item is announced).
                break;
            default:
                Reply(ServerReplyKind.AdminCommandResult, $"Could not find command {parts[0].TrimStart('/')}.");
                break;
        }
    }

    private readonly Dictionary<string, GameDataNames> _gameData = new(StringComparer.OrdinalIgnoreCase);

    public void SetGameData(string game, GameDataNames data) => _gameData[game] = data;

    public Task<GameDataNames?> GetGameDataAsync(GroupViewModel group, string game) =>
        Task.FromResult(_gameData.TryGetValue(game, out var data) ? data : null);

    public RoomSettingsSnapshot? RoomSettings { get; set; }

    public RoomSettingsSnapshot? GetRoomSettings(GroupViewModel group) => group.IsLeaderConnected ? RoomSettings : null;

    public Task InitializeGroupAsync(GroupViewModel group)
    {
        // Mark every slot processed so the startup-sync banner doesn't hang at 0/N.
        foreach (var slot in group.Group.Slots.ToList())
        {
            SlotInitialSyncCompleted?.Invoke(group, slot);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<PlayerInfo> RoomPlayers { get; set; } = Array.Empty<PlayerInfo>();

    public Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group) =>
        Task.FromResult(RoomPlayers);

    private readonly Dictionary<Guid, List<HintableLocation>> _hintableLocationsBySlot = new();

    public List<(Guid SlotId, long LocationId)> SentLocationHints { get; } = new();
    public List<(Guid SlotId, string ItemName)> SentItemHints { get; } = new();

    public void SetHintableLocations(SlotProfile slot, IReadOnlyList<HintableLocation> locations) =>
        _hintableLocationsBySlot[slot.Id] = locations.ToList();

    public Task<IReadOnlyList<HintableLocation>> GetHintableLocationsAsync(GroupViewModel group, SlotProfile slot) =>
        Task.FromResult<IReadOnlyList<HintableLocation>>(
            _hintableLocationsBySlot.TryGetValue(slot.Id, out var locations) ? locations : Array.Empty<HintableLocation>());

    public Task SendHintAsync(GroupViewModel group, SlotProfile slot, long locationId)
    {
        SentLocationHints.Add((slot.Id, locationId));

        // Real code keeps hinted locations in AllMissingLocations and HintPickerViewModel hides them via GroupViewModel.Hints;
        // this fake has no hint list, so it approximates the effect by removing the location here.
        if (_hintableLocationsBySlot.TryGetValue(slot.Id, out var locations))
        {
            _hintableLocationsBySlot[slot.Id] = locations.Where(l => l.LocationId != locationId).ToList();
        }

        return Task.CompletedTask;
    }

    public Task SendItemHintAsync(GroupViewModel group, SlotProfile slot, string itemName)
    {
        SentItemHints.Add((slot.Id, itemName));
        return Task.CompletedTask;
    }

    private readonly Dictionary<Guid, List<string>> _hintableItemsBySlot = new();

    public int GetHintableItemsCallCount { get; private set; }

    public void SetHintableItems(SlotProfile slot, IReadOnlyList<string> itemNames) =>
        _hintableItemsBySlot[slot.Id] = itemNames.ToList();

    public Task<IReadOnlyList<string>> GetHintableItemsAsync(GroupViewModel group, SlotProfile slot)
    {
        GetHintableItemsCallCount++;
        return Task.FromResult<IReadOnlyList<string>>(
            _hintableItemsBySlot.TryGetValue(slot.Id, out var items) ? items : Array.Empty<string>());
    }

    public List<SlotProfile> ReleasedHeldSessionSlots { get; } = new();

    public Task ReleaseHeldSessionAsync(GroupViewModel group, SlotProfile slot)
    {
        ReleasedHeldSessionSlots.Add(slot);
        return Task.CompletedTask;
    }
}
