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
/// Kategorie B (Test-Umsetzungsplan.md): <see cref="Archipolygo.Services.ConnectionManager"/>'s
/// <c>GetHintableLocationsAsync</c>/<c>GetHintableItemsAsync</c>/
/// <c>SendHintAsync</c>/<c>SendItemHintAsync</c>/<c>ReleaseHeldSessionAsync</c>
/// (see Feature-Plaene/Archiv/Hint-Eingabefeld.md) - reusing the leader
/// session when the target slot already is the leader, otherwise a brief
/// probe-connect that never touches the group's actual leader and (per that
/// plan's "Status" section) stays open afterward until
/// <c>ReleaseHeldSessionAsync</c> actually tears it down, rather than
/// reconnecting from scratch for every single browse/send of the same slot.
/// All serialized against <c>SwitchLeaderAsync</c>/<c>CatchUpSyncAsync</c> via
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

    /// <summary>
    /// The scenario the missing-locations cache actually exists for: a
    /// sibling slot that got its own brief catch-up dip at startup (see
    /// SwitchLeaderAsync's sibling sweep) - a session opened and torn back
    /// down again by that sweep itself, *not* kept alive by anything the
    /// Hint picker did. Browsing it afterward must still be instant, purely
    /// from the cache that catch-up dip already populated - no new probe
    /// session at all, unlike <see cref="GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader_AndStaysHeld"/>
    /// where the *first* browse is what creates and holds the session.
    /// </summary>
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

        // Alice's leader connect triggers the sibling catch-up sweep, which
        // briefly connects and disconnects Bob - nothing to do with the Hint
        // picker at all.
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
        // Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section: the
        // probe now stays open (kept alive for the Hint picker) instead of
        // disconnecting immediately - see ReleaseHeldSessionAsync below for
        // how it eventually gets torn down.
        Assert.Equal(0, bobSession.Socket.DisconnectCallCount);
        Assert.Equal(new[] { "Bob's Chest" }, locations.Select(l => l.Name));

        // A second browse for the very same slot doesn't reconnect either -
        // served straight from the missing-locations cache this first call
        // already populated.
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

    /// <summary>
    /// Dev question, verified against the official network protocol: the
    /// game-name lookup, the RoomInfo checksum, and the DataPackage exchange
    /// are all room-wide information, not tied to which slot is asking (see
    /// GetHintableItemsAsync's own doc comment). So browsing Item mode for a
    /// non-leader slot, while the leader is already connected, needs no
    /// connection for that slot at all - it's answered entirely through the
    /// already-open leader session.
    /// </summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task ItemMode_NonLeaderSlot_LeaderAlreadyConnected_AnswersViaLeaderWithNoNewConnection()
    {
        var (manager, factory) = MakeManager();
        // Alice-only at connect time - Bob is added only afterward, so the
        // automatic sibling catch-up sweep never touches him (same reasoning
        // as GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader_AndStaysHeld);
        // this test is specifically about Bob needing *no* connection of his
        // own at all, which a sweep-triggered one would quietly undermine.
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        // The leader's own roster already covers every other configured slot
        // too - same room-wide roster BuildSlotRoster already relies on for
        // hint/alias resolution.
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

    /// <summary>
    /// Sending, unlike browsing, genuinely needs to go through the target
    /// slot's own session - the "!hint" chat command is tied to whichever
    /// connection sent it. Held-connection behavior (Feature-Plaene/Archiv/Hint-Eingabefeld.md's
    /// "Status" section) still applies once that connection exists: a second
    /// send for the same slot reuses it instead of reconnecting.
    /// </summary>
    [AvaloniaFact(Timeout = 5000)]
    public async Task SendItemHintAsync_NonLeaderSlot_ConnectsOnce_ReusesForASecondSend()
    {
        var (manager, factory) = MakeManager();
        // Alice-only at connect time - see the identical reasoning in
        // ItemMode_NonLeaderSlot_LeaderAlreadyConnected_AnswersViaLeaderWithNoNewConnection.
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
        // Alice-only at connect time - see the identical reasoning in
        // GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader_AndStaysHeld.
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

        // A second hint for the same slot right after reuses the still-held
        // session - no second connect.
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
        // GetHintableLocationsAsync_NonLeaderSlot_ProbeConnectsWithoutTouchingLeader_AndStaysHeld;
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
