using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;

namespace Archipolygo.TestSupport;

/// <summary>
/// Fake <see cref="IArchipelagoSession"/> for Kategorie B (Test-Umsetzungsplan.md) -
/// exercises <see cref="Archipolygo.Services.ConnectionManager"/>'s real
/// locking/ordering logic against a fully controllable "connection" instead
/// of a real Archipelago server. Only implements the members
/// <c>ConnectionManager</c> actually touches with real behavior; everything
/// else throws <see cref="NotImplementedException"/> deliberately - add real
/// behavior there only once a test actually needs it, per the plan's own
/// guidance, rather than pre-building a full fake of the whole client
/// library up front.
///
/// The one central control point: <see cref="LoginResultSource"/>, a
/// <see cref="TaskCompletionSource{TResult}"/> a test completes/faults
/// itself, so it can dictate exactly when (and how) a simulated connect
/// "finishes" - the basis for testing <c>ConnectionManager</c>'s overlap/
/// ordering/abort behavior deterministically, without any real
/// <c>Task.Delay</c>.
/// </summary>
public sealed class FakeArchipelagoSession : IArchipelagoSession
{
    public FakeArchipelagoSocketHelper Socket { get; } = new();
    IArchipelagoSocketHelper IArchipelagoSession.Socket => Socket;

    public FakeReceivedItemsHelper Items { get; } = new();
    IReceivedItemsHelper IArchipelagoSession.Items => Items;

    public FakeLocationCheckHelper Locations { get; } = new();
    ILocationCheckHelper IArchipelagoSession.Locations => Locations;

    public FakePlayerHelper Players { get; } = new();
    IPlayerHelper IArchipelagoSession.Players => Players;

    public IDataStorageHelper DataStorage => throw new NotImplementedException();

    public FakeConnectionInfoProvider ConnectionInfo { get; }
    IConnectionInfoProvider IArchipelagoSession.ConnectionInfo => ConnectionInfo;

    public IRoomStateHelper RoomState => throw new NotImplementedException();

    public FakeMessageLogHelper MessageLog { get; } = new();
    IMessageLogHelper IArchipelagoSession.MessageLog => MessageLog;

    public FakeHintsHelper Hints { get; } = new();
    IHintsHelper IArchipelagoSession.Hints => Hints;

    /// <summary>
    /// Every message <see cref="Say"/> was called with, in order - lets a
    /// test assert <c>ConnectionManager.SendMessageAsync</c> actually reached
    /// the group's current leader session and no other.
    /// </summary>
    public List<string> SentMessages { get; } = new();

    /// <summary>
    /// The externally-controllable result of this session's one
    /// <see cref="LoginAsync"/> call - see the class doc comment.
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> so a
    /// test completing this from the same thread that's awaiting
    /// <c>ConnectionManager</c>'s call doesn't deadlock/reenter synchronously.
    /// </summary>
    public TaskCompletionSource<LoginResult> LoginResultSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeArchipelagoSession(int numericSlot = 1)
    {
        ConnectionInfo = new FakeConnectionInfoProvider(numericSlot);
    }

    /// <summary>
    /// Always completes immediately - only <see cref="LoginAsync"/> is a
    /// deliberate control point (see the class doc comment); every Kategorie
    /// B scenario the plan describes hinges on when *login* resolves, not
    /// the socket-level connect underneath it.
    /// </summary>
    public Task<RoomInfoPacket> ConnectAsync() => Task.FromResult<RoomInfoPacket>(null!);

    public Task<LoginResult> LoginAsync(string game, string name, ItemsHandlingFlags itemsHandlingFlags,
        Version? version = null, string[]? tags = null, string? uuid = null, string? password = null, bool requestSlotData = true) =>
        LoginResultSource.Task;

    public LoginResult TryConnectAndLogin(string game, string name, ItemsHandlingFlags itemsHandlingFlags,
        Version? version = null, string[]? tags = null, string? uuid = null, string? password = null, bool requestSlotData = true) =>
        throw new NotImplementedException();

    public void Say(string message) => SentMessages.Add(message);

    public void SetClientState(ArchipelagoClientState state) => throw new NotImplementedException();

    public void SetGoalAchieved() => throw new NotImplementedException();

    /// <summary>Convenience for the common case - a test that doesn't care about controlling the login outcome, just wants it to succeed.</summary>
    public void CompleteLoginSuccessfully(int team = 0, int slot = 1) =>
        LoginResultSource.TrySetResult(new LoginSuccessful(new ConnectedPacket { Team = team, Slot = slot }));
}

/// <summary>
/// Fake <see cref="IArchipelagoSocketHelper"/> - only <see cref="SocketClosed"/>
/// (a test fires this to simulate an unexpected drop),
/// <see cref="ErrorReceived"/> (subscribed to but never raised in Kategorie B),
/// and <see cref="DisconnectAsync"/> (recorded via <see cref="DisconnectCallCount"/>,
/// so a test can assert a session was actually torn down) have real behavior.
/// </summary>
public sealed class FakeArchipelagoSocketHelper : IArchipelagoSocketHelper
{
    public event ArchipelagoSocketHelperDelagates.PacketReceivedHandler? PacketReceived;
    public event ArchipelagoSocketHelperDelagates.PacketsSentHandler? PacketsSent;
    public event ArchipelagoSocketHelperDelagates.ErrorReceivedHandler? ErrorReceived;
    public event ArchipelagoSocketHelperDelagates.SocketClosedHandler? SocketClosed;
    public event ArchipelagoSocketHelperDelagates.SocketOpenedHandler? SocketOpened;

    public Uri Uri => throw new NotImplementedException();

    public bool Connected { get; private set; } = true;

    public int DisconnectCallCount { get; private set; }

    public void SendPacket(ArchipelagoPacketBase packet) => throw new NotImplementedException();
    public void SendMultiplePackets(List<ArchipelagoPacketBase> packets) => throw new NotImplementedException();
    public void SendMultiplePackets(params ArchipelagoPacketBase[] packets) => throw new NotImplementedException();

    public Task ConnectAsync() => throw new NotImplementedException();

    public Task DisconnectAsync()
    {
        DisconnectCallCount++;
        Connected = false;
        return Task.CompletedTask;
    }

    public Task SendPacketAsync(ArchipelagoPacketBase packet) => throw new NotImplementedException();
    public Task SendMultiplePacketsAsync(List<ArchipelagoPacketBase> packets) => throw new NotImplementedException();
    public Task SendMultiplePacketsAsync(params ArchipelagoPacketBase[] packets) => throw new NotImplementedException();

    /// <summary>Simulates the leader's connection dropping unexpectedly - fires the same event a real dropped socket would.</summary>
    public void RaiseSocketClosed(string reason = "simulated drop") => SocketClosed?.Invoke(reason);
}

/// <summary>Fake <see cref="IReceivedItemsHelper"/> - never actually delivers items in Kategorie B (ordering-only), so only the subscribable event needs to exist.</summary>
public sealed class FakeReceivedItemsHelper : IReceivedItemsHelper
{
    public event ReceivedItemsHelper.ItemReceivedHandler? ItemReceived;

    public string GetItemName(long id, string? game = null) => throw new NotImplementedException();
    public int Index => throw new NotImplementedException();
    public ReadOnlyCollection<ItemInfo> AllItemsReceived => throw new NotImplementedException();
    public bool Any() => false;
    public ItemInfo PeekItem() => throw new NotImplementedException();
    public ItemInfo DequeueItem() => throw new NotImplementedException();
}

/// <summary>
/// Fake <see cref="ILocationCheckHelper"/> - <see cref="AllLocations"/>/
/// <see cref="AllLocationsChecked"/> are plain settable lists (a test
/// arranges whatever counts it wants <c>ConnectionManager</c> to read after a
/// simulated login), and <see cref="RaiseCheckedLocationsUpdated"/> lets a
/// test simulate a live update to the leader's own tracked session. Every
/// other member (checks/scouting/name lookups) is never exercised by
/// Kategorie B's ordering-only tests and throws deliberately, same "only what
/// ConnectionManager actually touches" principle as the other fakes in this file.
/// </summary>
public sealed class FakeLocationCheckHelper : ILocationCheckHelper
{
    public ReadOnlyCollection<long> AllLocations { get; set; } = new(Array.Empty<long>());

    public ReadOnlyCollection<long> AllLocationsChecked { get; set; } = new(Array.Empty<long>());

    public ReadOnlyCollection<long> AllMissingLocations =>
        new(AllLocations.Except(AllLocationsChecked).ToList());

    public event LocationCheckHelper.CheckedLocationsUpdatedHandler? CheckedLocationsUpdated;

    /// <summary>Simulates the server reporting newly-checked locations (e.g. a remote !collect) for this slot's session.</summary>
    public void RaiseCheckedLocationsUpdated(ReadOnlyCollection<long> newlyCheckedLocations) =>
        CheckedLocationsUpdated?.Invoke(newlyCheckedLocations);

    public void CompleteLocationChecks(params long[] ids) => throw new NotImplementedException();
    public Task CompleteLocationChecksAsync(params long[] ids) => throw new NotImplementedException();

    public Task<Dictionary<long, ScoutedItemInfo>> ScoutLocationsAsync(HintCreationPolicy hintCreationPolicy, params long[] ids) =>
        throw new NotImplementedException();
    public Task<Dictionary<long, ScoutedItemInfo>> ScoutLocationsAsync(bool createAsHint, params long[] ids) =>
        throw new NotImplementedException();
    public Task<Dictionary<long, ScoutedItemInfo>> ScoutLocationsAsync(params long[] ids) =>
        throw new NotImplementedException();

    public long GetLocationIdFromName(string game, string locationName) => throw new NotImplementedException();
    public string GetLocationNameFromId(long locationId, string? game = null) => throw new NotImplementedException();
}

/// <summary>Fake <see cref="IPlayerHelper"/> - <see cref="AllPlayers"/> defaults to empty (settable) since <c>ConnectionManager</c>'s roster-building runs unconditionally on every leader connect.</summary>
public sealed class FakePlayerHelper : IPlayerHelper
{
    public IEnumerable<PlayerInfo> AllPlayers { get; set; } = Array.Empty<PlayerInfo>();

    public ReadOnlyDictionary<int, ReadOnlyCollection<PlayerInfo>> Players => throw new NotImplementedException();
    public PlayerInfo ActivePlayer => throw new NotImplementedException();
    public string GetPlayerAlias(int slot) => throw new NotImplementedException();
    public string GetPlayerName(int slot) => throw new NotImplementedException();
    public string GetPlayerAliasAndName(int slot) => throw new NotImplementedException();
    public PlayerInfo GetPlayerInfo(int team, int slot) => throw new NotImplementedException();
    public PlayerInfo GetPlayerInfo(int slot) => throw new NotImplementedException();
}

/// <summary>Fake <see cref="IConnectionInfoProvider"/> - only <see cref="Slot"/> (the numeric slot id used to key roster lookups) has real, settable behavior.</summary>
public sealed class FakeConnectionInfoProvider : IConnectionInfoProvider
{
    public FakeConnectionInfoProvider(int slot) => Slot = slot;

    public string Game => string.Empty;
    public int Team => 0;
    public int Slot { get; }
    public string[] Tags => Array.Empty<string>();
    public ItemsHandlingFlags ItemsHandlingFlags => ItemsHandlingFlags.AllItems;
    public string Uuid => string.Empty;

    public void UpdateConnectionOptions(string[] tags) => throw new NotImplementedException();
    public void UpdateConnectionOptions(ItemsHandlingFlags itemsHandlingFlags) => throw new NotImplementedException();
    public void UpdateConnectionOptions(string[] tags, ItemsHandlingFlags itemsHandlingFlags) => throw new NotImplementedException();
}

/// <summary>Fake <see cref="IMessageLogHelper"/> - only leader sessions subscribe (see <c>ConnectionManager.ConnectSlotSessionAsync</c>); never raised in Kategorie B's ordering-only tests.</summary>
public sealed class FakeMessageLogHelper : IMessageLogHelper
{
    public event MessageLogHelper.MessageReceivedHandler? OnMessageReceived;
}

/// <summary>
/// Fake <see cref="IHintsHelper"/> - <see cref="TrackHints"/> just records
/// every subscription (per <paramref name="slot"/>, matching the real
/// per-sibling subscriptions <c>ConnectionManager</c> registers) rather than
/// ever actually invoking a callback; Kategorie B cares about *whether/how
/// many times* a connect subscribes, not hint content.
/// </summary>
public sealed class FakeHintsHelper : IHintsHelper
{
    public int TrackHintsCallCount { get; private set; }

    public void TrackHints(Action<Hint[]> onHintsUpdated, bool retrieveCurrentlyUnlockedHints = true, int? slot = null, int? team = null) =>
        TrackHintsCallCount++;

    public void CreateHints(int player, HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds) => throw new NotImplementedException();
    public void CreateHints(HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds) => throw new NotImplementedException();
    public void UpdateHintStatus(int player, long locationId, HintStatus newHintStatus) => throw new NotImplementedException();
    public Hint[] GetHints(int? slot = null, int? team = null) => throw new NotImplementedException();
    public Task<Hint[]> GetHintsAsync(int? slot = null, int? team = null) => throw new NotImplementedException();
}
