using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): <see cref="Archipolygo.Services.ConnectionManager"/>'s
/// <c>GetHintableLocationsAsync</c>/<c>SendHintAsync</c>/<c>SendItemHintAsync</c>
/// (see Feature-Plaene/Archiv/Hint-Eingabefeld.md) - reusing the leader
/// session when the target slot already is the leader, otherwise a brief
/// probe-connect that never touches the group's actual leader, all
/// serialized against <c>SwitchLeaderAsync</c>/<c>CatchUpSyncAsync</c> via
/// the same per-group gate (mirrors <see cref="ConnectionManagerCatchUpTests"/>'s
/// serialization tests, just for this new pair of operations).
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
        // The login is a generic "Tracker" client with an empty game (see
        // FakeConnectionInfoProvider.Game) - the real code resolves the
        // connected slot's own game via the room roster instead, so the fake
        // roster needs an entry for numeric slot 1 too.
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
        // Deliberately no LocationNames[99] entry.
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var locations = await manager.GetHintableLocationsAsync(vm, alice);

        Assert.Equal("Location #99", Assert.Single(locations).Name);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader()
    {
        var (manager, factory) = MakeManager();
        // Alice is the group's only slot at connect time - no automatic
        // sibling catch-up sweep to entangle this test with (see
        // ConnectionManagerCatchUpTests' identical reasoning). Bob is added
        // only afterward, purely for this test's own explicit probe-connect.
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
        Assert.Equal(1, bobSession.Socket.DisconnectCallCount); // probe torn back down afterward
        Assert.Equal(new[] { "Bob's Chest" }, locations.Select(l => l.Name));
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
    public async Task SendHintAsync_NonLeaderSlot_ProbeConnectsThenDisconnects_LeaderUntouched()
    {
        var (manager, factory) = MakeManager();
        // Alice-only at connect time - see the identical reasoning in
        // GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader.
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

        Assert.Equal(new long[] { 7 }, Assert.Single(bobSession.Hints.CreateHintsCalls));
        Assert.Empty(aliceSession.Hints.CreateHintsCalls); // never sent through the leader
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
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

    /// <summary>
    /// Two different non-leader slots both wanting a brief probe-connect at
    /// the same time must still serialize through the group's one-at-a-time
    /// connection slot, same invariant as <see cref="ConnectionManagerCatchUpTests"/>'s
    /// sweep-vs-CatchUpSyncAsync tests - just exercised here via
    /// GetHintableLocationsAsync instead.
    /// </summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task GetHintableLocationsAsync_ForDifferentNonLeaderSlots_SerializesInsteadOfRacing()
    {
        var (manager, factory) = MakeManager();
        // Alice-only at connect time - see the identical reasoning in
        // GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader;
        // Bob and Carol are added only afterward, so the automatic sibling
        // sweep never touches their (deliberately not-yet-completed) sessions.
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
