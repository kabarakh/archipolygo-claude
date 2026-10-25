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
/// Fake session for exercising ConnectionManager's locking/ordering without a server. Members ConnectionManager
/// doesn't touch throw deliberately; <see cref="LoginResultSource"/> lets a test decide when/how a connect finishes.
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

    public List<string> SentMessages { get; } = new();

    /// <summary>Completed by the test. RunContinuationsAsynchronously so completing it doesn't reenter ConnectionManager synchronously.</summary>
    public TaskCompletionSource<LoginResult> LoginResultSource { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeArchipelagoSession(int numericSlot = 1)
    {
        ConnectionInfo = new FakeConnectionInfoProvider(numericSlot);
    }

    public RoomInfoPacket? RoomInfoToReturn { get; set; }

    /// <summary>Always completes immediately - only login is a control point.</summary>
    public Task<RoomInfoPacket> ConnectAsync() => Task.FromResult<RoomInfoPacket>(RoomInfoToReturn!);

    public string? LastPasswordArgument { get; private set; }

    /// <summary>Shows whether a connect got past ConnectionManager's app-wide connect gate (CreateSession runs before it).</summary>
    public int LoginCallCount { get; private set; }

    public Task<LoginResult> LoginAsync(string game, string name, ItemsHandlingFlags itemsHandlingFlags,
        Version? version = null, string[]? tags = null, string? uuid = null, string? password = null, bool requestSlotData = true)
    {
        LoginCallCount++;
        LastPasswordArgument = password;
        return LoginResultSource.Task;
    }

    public LoginResult TryConnectAndLogin(string game, string name, ItemsHandlingFlags itemsHandlingFlags,
        Version? version = null, string[]? tags = null, string? uuid = null, string? password = null, bool requestSlotData = true) =>
        throw new NotImplementedException();

    public void Say(string message) => SentMessages.Add(message);

    public void SetClientState(ArchipelagoClientState state) => throw new NotImplementedException();

    public void SetGoalAchieved() => throw new NotImplementedException();

    public void CompleteLoginSuccessfully(int team = 0, int slot = 1) =>
        LoginResultSource.TrySetResult(new LoginSuccessful(new ConnectedPacket { Team = team, Slot = slot }));
}

/// <summary>Only SocketClosed, DisconnectAsync, SendPacket and PacketReceived have real (recorded/raisable) behavior.</summary>
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

    public List<ArchipelagoPacketBase> SentPackets { get; } = new();

    public void SendPacket(ArchipelagoPacketBase packet) => SentPackets.Add(packet);
    public void SendMultiplePackets(List<ArchipelagoPacketBase> packets) => throw new NotImplementedException();
    public void SendMultiplePackets(params ArchipelagoPacketBase[] packets) => throw new NotImplementedException();

    public void RaisePacketReceived(ArchipelagoPacketBase packet) => PacketReceived?.Invoke(packet);

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

    public void RaiseSocketClosed(string reason = "simulated drop") => SocketClosed?.Invoke(reason);
}

/// <summary>Never delivers items; only the event exists.</summary>
public sealed class FakeReceivedItemsHelper : IReceivedItemsHelper
{
    public event ReceivedItemsHelper.ItemReceivedHandler? ItemReceived;

    public string GetItemName(long id, string? game = null) => null!;
    public int Index => throw new NotImplementedException();
    public ReadOnlyCollection<ItemInfo> AllItemsReceived => throw new NotImplementedException();
    public bool Any() => false;
    public ItemInfo PeekItem() => throw new NotImplementedException();
    public ItemInfo DequeueItem() => throw new NotImplementedException();
}

/// <summary>Location lists are settable; members ConnectionManager doesn't touch throw deliberately.</summary>
public sealed class FakeLocationCheckHelper : ILocationCheckHelper
{
    public ReadOnlyCollection<long> AllLocations { get; set; } = new(Array.Empty<long>());

    public ReadOnlyCollection<long> AllLocationsChecked { get; set; } = new(Array.Empty<long>());

    public ReadOnlyCollection<long> AllMissingLocations =>
        new(AllLocations.Except(AllLocationsChecked).ToList());

    public event LocationCheckHelper.CheckedLocationsUpdatedHandler? CheckedLocationsUpdated;

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

    /// <summary>An id with no entry returns null, exercising the real <c>?? $"Location #{id}"</c> fallback.</summary>
    public Dictionary<long, string> LocationNames { get; } = new();

    public string GetLocationNameFromId(long locationId, string? game = null) =>
        LocationNames.TryGetValue(locationId, out var name) ? name : null!;
}

public sealed class FakePlayerHelper : IPlayerHelper
{
    public IEnumerable<PlayerInfo> AllPlayers { get; set; } = Array.Empty<PlayerInfo>();

    public ReadOnlyDictionary<int, ReadOnlyCollection<PlayerInfo>> Players => throw new NotImplementedException();
    public PlayerInfo ActivePlayer => throw new NotImplementedException();
    public string GetPlayerAlias(int slot) => throw new NotImplementedException();
    public string GetPlayerName(int slot) => throw new NotImplementedException();
    public string GetPlayerAliasAndName(int slot) => throw new NotImplementedException();
    public PlayerInfo GetPlayerInfo(int team, int slot) => throw new NotImplementedException();

    /// <summary>Needed because <see cref="FakeConnectionInfoProvider.Game"/> is always empty.</summary>
    public PlayerInfo GetPlayerInfo(int slot) => AllPlayers.FirstOrDefault(p => p.Slot == slot)!;
}

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

public sealed class FakeMessageLogHelper : IMessageLogHelper
{
    public event MessageLogHelper.MessageReceivedHandler? OnMessageReceived;
}

/// <summary>TrackHints only records subscriptions per slot and never invokes the callback.</summary>
public sealed class FakeHintsHelper : IHintsHelper
{
    public int TrackHintsCallCount { get; private set; }

    public void TrackHints(Action<Hint[]> onHintsUpdated, bool retrieveCurrentlyUnlockedHints = true, int? slot = null, int? team = null) =>
        TrackHintsCallCount++;

    public List<long[]> CreateHintsCalls { get; } = new();

    public void CreateHints(int player, HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds) => CreateHintsCalls.Add(locationIds);
    public void CreateHints(HintStatus hintStatus = HintStatus.Unspecified, params long[] locationIds) => CreateHintsCalls.Add(locationIds);
    public void UpdateHintStatus(int player, long locationId, HintStatus newHintStatus) => throw new NotImplementedException();
    public Hint[] GetHints(int? slot = null, int? team = null) => throw new NotImplementedException();
    public Task<Hint[]> GetHintsAsync(int? slot = null, int? team = null) => throw new NotImplementedException();
}
