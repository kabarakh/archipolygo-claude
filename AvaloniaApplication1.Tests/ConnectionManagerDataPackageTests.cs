using System;
using System.IO;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): <see cref="ConnectionManager.GetHintableItemsAsync"/> -
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section (the Item-mode
/// candidate pool became the game's full DataPackage item list, checksum-
/// cached to disk per group). Uses a real <see cref="PersistenceService"/>
/// against a temp directory (see <see cref="PersistenceServiceTests"/>'s
/// identical pattern) rather than <see cref="FakePersistenceService"/> (which
/// deliberately no-ops every save) - the whole point here is proving the
/// cache actually round-trips and is actually consulted.
/// </summary>
public sealed class ConnectionManagerDataPackageTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _persistenceService;

    public ConnectionManagerDataPackageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        _persistenceService = new PersistenceService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private ConnectionManager MakeManager(out FakeSessionFactory factory)
    {
        factory = new FakeSessionFactory();
        return new ConnectionManager(
            new NoOpMessageHistoryService(),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero,
            persistenceService: _persistenceService,
            dataPackageRequestTimeout: TimeSpan.FromMilliseconds(200));
    }

    private static FakeArchipelagoSession MakeConnectedSession(string game, string? checksum)
    {
        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[] { new PlayerInfo(0, 1, "Alice", "Alice", game, null, null) };
        if (checksum is not null)
        {
            session.RoomInfoToReturn = new RoomInfoPacket
            {
                DataPackageChecksums = new() { [game] = checksum }
            };
        }

        return session;
    }

    private static DataPackagePacket MakeDataPackageResponse(string game, params string[] itemNames)
    {
        var lookup = new System.Collections.Generic.Dictionary<string, long>();
        for (var i = 0; i < itemNames.Length; i++)
        {
            lookup[itemNames[i]] = i + 1;
        }

        return new DataPackagePacket
        {
            DataPackage = new DataPackage { Games = { [game] = new GameData { ItemLookup = lookup } } }
        };
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task NoCacheYet_RequestsTheRightGame_ReturnsItemNamesAndCachesThem()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = MakeConnectedSession("Kirby Super Star", checksum: "abc123");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var task = manager.GetHintableItemsAsync(vm, alice);

        var sentPacket = Assert.Single(session.Socket.SentPackets);
        var getDataPackagePacket = Assert.IsType<GetDataPackagePacket>(sentPacket);
        Assert.Equal(new[] { "Kirby Super Star" }, getDataPackagePacket.Games);

        session.Socket.RaisePacketReceived(MakeDataPackageResponse("Kirby Super Star", "Fire Rod", "Ice Rod"));
        var items = await task;

        Assert.Equal(new[] { "Fire Rod", "Ice Rod" }, items);

        var cached = _persistenceService.LoadDataPackageCache(group.Id, "Kirby Super Star");
        Assert.NotNull(cached);
        Assert.Equal("abc123", cached!.Checksum);
        Assert.Equal(new[] { "Fire Rod", "Ice Rod" }, cached.ItemNames);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ChecksumUnchanged_UsesCacheWithoutAskingTheServerAgain()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        _persistenceService.SaveDataPackageCache(group.Id, "Kirby Super Star",
            new Archipolygo.Models.DataPackageCacheEntry { Checksum = "same-checksum", ItemNames = { "Cutter", "Cook" } });

        var session = MakeConnectedSession("Kirby Super Star", checksum: "same-checksum");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var items = await manager.GetHintableItemsAsync(vm, alice);

        Assert.Equal(new[] { "Cutter", "Cook" }, items);
        Assert.Empty(session.Socket.SentPackets); // never had to ask the server at all
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ChecksumChanged_IgnoresStaleCacheAndRefetches()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        _persistenceService.SaveDataPackageCache(group.Id, "Kirby Super Star",
            new Archipolygo.Models.DataPackageCacheEntry { Checksum = "old-checksum", ItemNames = { "Stale Item" } });

        var session = MakeConnectedSession("Kirby Super Star", checksum: "new-checksum");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var task = manager.GetHintableItemsAsync(vm, alice);
        Assert.Single(session.Socket.SentPackets); // did have to ask this time
        session.Socket.RaisePacketReceived(MakeDataPackageResponse("Kirby Super Star", "Fresh Item"));
        var items = await task;

        Assert.Equal(new[] { "Fresh Item" }, items);
        Assert.Equal("new-checksum", _persistenceService.LoadDataPackageCache(group.Id, "Kirby Super Star")!.Checksum);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task NoChecksumAvailable_AlwaysRefetches_NeverCaches()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = MakeConnectedSession("Kirby Super Star", checksum: null); // RoomInfoToReturn stays null
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var task = manager.GetHintableItemsAsync(vm, alice);
        session.Socket.RaisePacketReceived(MakeDataPackageResponse("Kirby Super Star", "Fire Rod"));
        var items = await task;

        Assert.Equal(new[] { "Fire Rod" }, items);
        Assert.Null(_persistenceService.LoadDataPackageCache(group.Id, "Kirby Super Star"));
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ServerNeverResponds_TimesOutToEmptyListInsteadOfHanging()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = MakeConnectedSession("Kirby Super Star", checksum: "abc");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var items = await manager.GetHintableItemsAsync(vm, alice); // never raises a response

        Assert.Empty(items);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ResponseForADifferentGame_ReturnsEmptyList()
    {
        var manager = MakeManager(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = MakeConnectedSession("Kirby Super Star", checksum: "abc");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var task = manager.GetHintableItemsAsync(vm, alice);
        session.Socket.RaisePacketReceived(MakeDataPackageResponse("A Different Game", "Something"));
        var items = await task;

        Assert.Empty(items);
    }
}
