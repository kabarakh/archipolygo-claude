using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Hint picker operations: reuse the leader session for the leader slot, else a probe-connect that never touches the
/// leader and stays held until ReleaseHeldSessionAsync; all serialized on the per-group gate.
/// </summary>
public class ConnectionManagerHintTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_LeaderSlot_ReusesSessionAndReturnsNamedMissingLocations()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        // The fake's ConnectionInfo.Game is empty; the real code resolves the game via the roster, so it needs slot 1.
        session.Players.AllPlayers = new[] { new PlayerInfo(0, 1, "Alice", "Alice", "Kirby Super Star", null, null) };
        session.Locations.AllLocations = new ReadOnlyCollection<long>(new long[] { 1, 2, 3 });
        session.Locations.AllLocationsChecked = new ReadOnlyCollection<long>(new long[] { 1 });
        session.Locations.LocationNames[2] = "Cave Entrance - Chest";
        session.Locations.LocationNames[3] = "Forest Clearing - Tree Stump";
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var locations = await manager.GetHintableLocationsAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, factory.CreatedSessions.Count); // no extra connect - reused the leader session
        Assert.Equal(
            new[] { (2L, "Cave Entrance - Chest"), (3L, "Forest Clearing - Tree Stump") },
            locations.Select(l => (l.LocationId, l.Name)).OrderBy(t => t.Item1).ToArray());
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_UnnamedLocation_FallsBackToPlaceholderName()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[] { new PlayerInfo(0, 1, "Alice", "Alice", "Some Game", null, null) };
        session.Locations.AllLocations = new ReadOnlyCollection<long>(new long[] { 99 });
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var locations = await manager.GetHintableLocationsAsync(vm, alice);

        Assert.Equal("Location #99", Assert.Single(locations).Name);
    }

    /// <summary>A slot already dipped in by the startup sibling sweep is answered from the missing-locations cache.</summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_SlotAlreadyCaughtUpAtStartup_AnswersFromCacheWithNoNewSession()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        bobSession.Players.AllPlayers = new[] { new PlayerInfo(0, 2, "Bob", "Bob", "Some Game", null, null) };
        bobSession.Locations.AllLocations = new ReadOnlyCollection<long>(new long[] { 10 });
        bobSession.Locations.LocationNames[10] = "Bob's Chest";
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's startup catch-up dip
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount); // that dip already closed itself

        var locations = await manager.GetHintableLocationsAsync(vm, bob);

        Assert.Equal(2, factory.CreatedSessions.Count); // no new session just to browse
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount); // and nothing new to tear down either
        Assert.Equal(new[] { "Bob's Chest" }, locations.Select(l => l.Name));
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader_AndStaysHeld()
    {
        var (manager, factory) = MakeManager();
        // Bob is added after connect so the sibling catch-up sweep doesn't interfere.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        bobSession.Players.AllPlayers = new[] { new PlayerInfo(0, 2, "Bob", "Bob", "Some Game", null, null) };
        bobSession.Locations.AllLocations = new ReadOnlyCollection<long>(new long[] { 10 });
        bobSession.Locations.LocationNames[10] = "Bob's Chest";
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        var locations = await manager.GetHintableLocationsAsync(vm, bob);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, factory.CreatedSessions.Count); // alice (leader, reused) + bob's own probe-connect
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount); // leader never touched
        Assert.Equal(alice.Id, vm.LeaderSlotId); // still the leader
        // The probe stays held for the Hint picker until ReleaseHeldSessionAsync.
        Assert.Equal(0, bobSession.Socket.DisconnectCallCount);
        Assert.Equal(new[] { "Bob's Chest" }, locations.Select(l => l.Name));

        // Served from the missing-locations cache.
        var locationsAgain = await manager.GetHintableLocationsAsync(vm, bob);
        Assert.Equal(2, factory.CreatedSessions.Count);
        Assert.Equal(new[] { "Bob's Chest" }, locationsAgain.Select(l => l.Name));

        await manager.ReleaseHeldSessionAsync(vm, bob);
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount); // now actually torn down
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount); // still never touched
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ReleaseHeldSessionAsync_CurrentLeader_NeverDisconnectsIt()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        await manager.ReleaseHeldSessionAsync(vm, alice);

        Assert.Equal(0, session.Socket.DisconnectCallCount);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
    }

    /// <summary>Game name, checksum and DataPackage are room-wide, so a non-leader slot needs no connection of its own.</summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task ItemMode_NonLeaderSlot_LeaderAlreadyConnected_AnswersViaLeaderWithNoNewConnection()
    {
        var (manager, factory) = MakeManager();
        // Bob is added after connect so the sibling sweep never gives him a connection.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 1, "Alice", "Alice", "Kirby Super Star", null, null),
            new PlayerInfo(0, 2, "Bob", "Bob", "Some Game", null, null),
        };
        aliceSession.RoomInfoToReturn = new RoomInfoPacket { DataPackageChecksums = new() { ["Some Game"] = "abc" } };
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var itemsTask = manager.GetHintableItemsAsync(vm, bob);
        aliceSession.Socket.RaisePacketReceived(new DataPackagePacket
        {
            DataPackage = new DataPackage
            {
                Games = new Dictionary<string, GameData> { ["Some Game"] = new() { ItemLookup = new Dictionary<string, long> { ["Sword"] = 1 } } }
            }
        });
        var items = await itemsTask;

        Assert.Equal(1, factory.CreatedSessions.Count); // alice only - bob never connected at all
        Assert.Equal(new[] { "Sword" }, items);
    }

    /// <summary>Sending needs the target slot's own session ("!hint" is tied to the sending connection).</summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task SendItemHintAsync_NonLeaderSlot_ConnectsOnce_ReusesForASecondSend()
    {
        var (manager, factory) = MakeManager();
        // Bob is added after connect so the sibling sweep doesn't interfere.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 1, "Alice", "Alice", "Kirby Super Star", null, null),
            new PlayerInfo(0, 2, "Bob", "Bob", "Some Game", null, null),
        };
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        await manager.SendItemHintAsync(vm, bob, "Sword");
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's one connect to actually send as Bob
        Assert.Equal(0, bobSession.Socket.DisconnectCallCount);

        await manager.SendItemHintAsync(vm, bob, "Shield");
        Assert.Equal(2, factory.CreatedSessions.Count); // reused, not reconnected
        Assert.Equal(new[] { "!hint Sword", "!hint Shield" }, bobSession.SentMessages);

        await manager.ReleaseHeldSessionAsync(vm, bob);
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount);
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount); // leader never touched
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_LoginFails_ReturnsEmptyList()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.LoginResultSource.SetResult(new LoginFailure("bad password")); // a real rejection - never retried

        var locations = await manager.GetHintableLocationsAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(locations);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SendHintAsync_LeaderSlot_ReusesSessionAndCallsCreateHintsWithLocationId()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        await manager.SendHintAsync(vm, alice, locationId: 42);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, factory.CreatedSessions.Count); // reused the leader session
        Assert.Equal(new long[] { 42 }, Assert.Single(session.Hints.CreateHintsCalls));
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SendHintAsync_NonLeaderSlot_ProbeConnectsAndStaysHeld_LeaderUntouched()
    {
        var (manager, factory) = MakeManager();
        // Bob is added after connect so the sibling sweep doesn't interfere.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        await manager.SendHintAsync(vm, bob, locationId: 7);
        Dispatcher.UIThread.RunJobs();

        await manager.SendHintAsync(vm, bob, locationId: 8);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new long[] { 7, 8 }, bobSession.Hints.CreateHintsCalls.SelectMany(ids => ids));
        Assert.Empty(aliceSession.Hints.CreateHintsCalls); // never sent through the leader
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's one probe-connect, not two
        Assert.Equal(0, bobSession.Socket.DisconnectCallCount); // kept alive, not torn down per send

        await manager.ReleaseHeldSessionAsync(vm, bob);
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SendItemHintAsync_SendsHintChatCommand()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        await manager.SendItemHintAsync(vm, alice, "Fire Rod");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "!hint Fire Rod" }, session.SentMessages);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SendItemHintAsync_BlankName_NoOp()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        await manager.SendItemHintAsync(vm, alice, "   ");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(session.SentMessages);
        Assert.Equal(1, factory.CreatedSessions.Count); // never even tried to connect for a blank name
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_ForDifferentNonLeaderSlots_SerializesInsteadOfRacing()
    {
        var (manager, factory) = MakeManager();
        // Bob/Carol are added after connect so the sibling sweep never touches their uncompleted sessions.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        var carol = new SlotProfile { GroupId = group.Id, SlotName = "Carol" };
        group.Slots.Add(bob);
        group.Slots.Add(carol);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var carolSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(carolSession);

        var bobTask = manager.GetHintableLocationsAsync(vm, bob);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's probe-connect in flight

        var carolTask = manager.GetHintableLocationsAsync(vm, carol);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // carol queued behind bob, not raced in

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await bobTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Same(carolSession, factory.CreatedSessions[2]);

        carolSession.CompleteLoginSuccessfully(slot: 3);
        await carolTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount); // leader untouched throughout
    }
}
