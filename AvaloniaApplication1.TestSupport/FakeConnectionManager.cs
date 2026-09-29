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
/// No-network stand-in for <see cref="IConnectionManager"/> - shared by
/// TestHarness (see .claude/skills/ui-feature-prototyp) and
/// AvaloniaApplication1.Tests (see Test-Umsetzungsplan.md) so it isn't
/// duplicated between the two. Never opens a real Archipelago session;
/// "connecting" a slot just flips the view model's own state so the UI reads
/// as connected. Test code adds synthetic <see cref="EventEntry"/>/
/// <see cref="HintEntry"/>/<see cref="ReceivedItemEntry"/> directly to a
/// GroupViewModel's collections rather than going through this class.
///
/// The one exception is hint routing (see <see cref="_hintsBySlot"/> and
/// <see cref="SimulateHintLocation"/> below): that logic in the real
/// <c>ConnectionManager</c> is subtle enough (a real bug shipped in 0.1.0,
/// see the ConnectionManager.cs history around TrackHints) that it's worth
/// modeling here too, so the "leader must also track every sibling slot's own
/// hints" behavior can be demonstrated and re-checked without a real
/// Archipelago server. It's a deliberately independent re-implementation of
/// the same design, not the production code itself - it can't catch a
/// regression in ConnectionManager.cs directly, only show whether the
/// *design* (mirrored here) still produces the right result.
/// </summary>
public sealed class FakeConnectionManager : IConnectionManager
{
    public event Action<GroupViewModel>? GroupPersistNeeded;
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    // Never raised: this fake never announces a sync batch before its
    // SlotInitialSyncCompleted calls (see InitializeGroupAsync below), so
    // MainWindowViewModel's StartupSyncTotal never grows and the "Catching
    // up slots: N/M" banner simply never shows here - fine for this fake's
    // purpose (see the class doc comment), just declared to satisfy
    // IConnectionManager.
    public event Action<int>? SlotSyncBatchStarting;

    // Never invoked by this fake: nothing here ever calls SlotProfile.RequiresPassword
    // true / leaves a SlotProfile.Password empty in a way that would need it
    // (SwitchLeaderAsync below always "succeeds" unconditionally, no real
    // login). Declared purely to satisfy IConnectionManager - see
    // Feature-Plaene/Passwort-Speicherung.md.
    public Func<GroupViewModel, SlotProfile, bool, CancellationToken, Task<bool>>? PasswordRequested { get; set; }

    public event Action<GroupViewModel, ServerReplyKind, string>? ServerReplyReceived;
    public event Action<GroupViewModel>? LeaderConnected;

    // --- Fake hint room -----------------------------------------------
    //
    // Models the real Archipelago server's per-slot hints_{team}_{slot}
    // DataStorage keys (see MultiServer.py's notify_hints/on_new_hint): a
    // hint is stored under *both* the finding slot's and the receiving
    // slot's own key, and only clients that specifically subscribed to a
    // given key are notified when it changes - unlike chat/item-send lines,
    // there is no room-wide broadcast. _hintsBySlot/_hintSubscriptions are
    // keyed by a synthetic numeric "Archipelago slot id" (see
    // <see cref="NumericSlotId"/>), the same way the real code keys off
    // ArchipelagoSession's numeric ConnectionInfo.Slot.
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

        // Mirrors ConnectionManager.ConnectSlotSessionAsync's TrackHints
        // calls: one subscription for the leader's own slot (always
        // present), plus - the fix this models - one explicit subscription
        // per OTHER configured sibling slot in the same group, all over
        // this one "connection". Re-subscribing from scratch on every
        // leader switch keeps this in sync with whichever slot just became
        // leader, same as a real reconnect would.
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

        // retrieveCurrentlyUnlockedHints: true - replay anything already
        // recorded under this key before the subscription existed.
        if (_hintsBySlot.TryGetValue(numericId, out var existing) && existing.Count > 0)
        {
            OnUpdated(existing);
        }
    }

    /// <summary>
    /// Stand-in for a player running "!hint_location" (or any other hint
    /// creation): <paramref name="finder"/> is the configured slot whose
    /// world the hinted location lives in; <paramref name="receiverName"/>
    /// is whoever owns the item there - which, as in the bug report this
    /// models, need not be one of this group's own configured slots at all.
    /// Bidirectional storage + per-key notification only (see
    /// <see cref="Notify"/>) means this reproduces the exact failure mode:
    /// if nothing is subscribed to <paramref name="finder"/>'s own key (the
    /// pre-fix behavior when <paramref name="finder"/> isn't the leader),
    /// the hint is created but never routed anywhere - silently lost, same
    /// as it was in the real app before the ConnectionManager.cs fix.
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

        // If nobody currently tracks this key, the hint just sits in
        // _hintsBySlot unreported - exactly the pre-fix bug for a
        // non-leader sibling slot's own hints.
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

    /// <summary>Every line sent via <see cref="SendMessageAsync"/>, in order.</summary>
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

    // --- Fake admin server (Admin-Funktionen.md, feature-plan archive) --
    //
    // Just enough of MultiServer.py's "!admin" behavior for TestHarness and
    // tests to drive AdminPanelViewModel: login outcomes as CommandResult,
    // "must first login" when not logged in, failures/"/option" as
    // AdminCommandResult, successful actions only as a room-wide broadcast.
    // Replies are raised synchronously from SendMessageAsync.

    /// <summary>The fake server's server_password; null = "Remote administration is disabled".</summary>
    public string? AdminServerPassword { get; set; } = "admin";

    private readonly HashSet<Guid> _adminLoggedInGroups = new();

    /// <summary>Another client ran "!admin login" - the server silently drops this client's admin login.</summary>
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
        // Immediately mark every configured slot as "processed" so
        // MainWindowViewModel's startup-sync banner doesn't get stuck at
        // "Catching up slots: 0/N" forever - nothing here ever fires it.
        foreach (var slot in group.Group.Slots.ToList())
        {
            SlotInitialSyncCompleted?.Invoke(group, slot);
        }

        return Task.CompletedTask;
    }

    /// <summary>What <see cref="GetRoomPlayersAsync"/> returns - empty unless a test/TestHarness sets it.</summary>
    public IReadOnlyList<PlayerInfo> RoomPlayers { get; set; } = Array.Empty<PlayerInfo>();

    public Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group) =>
        Task.FromResult(RoomPlayers);

    // --- Hint picker (Feature-Plaene/Archiv/Hint-Eingabefeld.md) -------
    //
    // Same spirit as the rest of this fake: no real session, just enough
    // settable/recorded state for TestHarness (seed data via
    // SetHintableLocations) and future Kategorie B/C tests (assert against
    // SentLocationHints/SentItemHints) to exercise HintPickerViewModel
    // without a real Archipelago server.
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

        // Drop the just-hinted location from the seeded list too, so a
        // caller that reopens the picker (or re-reads SetHintableLocations'
        // backing list) sees it disappear, same as the real feature's
        // GetHintableLocationsAsync would once the location is no longer in
        // AllMissingLocations... except a hinted-but-unchecked location
        // never actually leaves AllMissingLocations for real - it's
        // HintPickerViewModel's own cross-reference against
        // GroupViewModel.Hints that hides it. This fake has no such hint
        // list of its own to cross-reference, so it approximates the same
        // visible effect by removing it here directly.
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

    /// <summary>Settable via <see cref="SetHintableItems"/> - see Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section for what this replaced.</summary>
    private readonly Dictionary<Guid, List<string>> _hintableItemsBySlot = new();

    /// <summary>How many times <see cref="GetHintableItemsAsync"/> was called, total across every slot - lets a test confirm <c>HintPickerViewModel</c> doesn't needlessly re-fetch (e.g. just toggling the exclude checkbox).</summary>
    public int GetHintableItemsCallCount { get; private set; }

    public void SetHintableItems(SlotProfile slot, IReadOnlyList<string> itemNames) =>
        _hintableItemsBySlot[slot.Id] = itemNames.ToList();

    public Task<IReadOnlyList<string>> GetHintableItemsAsync(GroupViewModel group, SlotProfile slot)
    {
        GetHintableItemsCallCount++;
        return Task.FromResult<IReadOnlyList<string>>(
            _hintableItemsBySlot.TryGetValue(slot.Id, out var items) ? items : Array.Empty<string>());
    }

    /// <summary>Every (group, slot) pair <see cref="ReleaseHeldSessionAsync"/> was called with, in order - lets a test confirm <c>HintPickerViewModel</c> releases the right slot at the right time (slot switch, picker close).</summary>
    public List<SlotProfile> ReleasedHeldSessionSlots { get; } = new();

    public Task ReleaseHeldSessionAsync(GroupViewModel group, SlotProfile slot)
    {
        ReleasedHeldSessionSlots.Add(slot);
        return Task.CompletedTask;
    }
}
