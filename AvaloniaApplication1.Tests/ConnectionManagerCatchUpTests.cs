using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

public class ConnectionManagerCatchUpTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task CatchUpSyncAsync_QueuesBehindInFlightSweep_InsteadOfConcurrentConnect()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        // Leader first with no siblings, so no sweep is involved.
        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        // Mirrors AddSlotsToGroup's individual CatchUpSyncAsync calls.
        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        var carol = new SlotProfile { GroupId = group.Id, SlotName = "Carol" };
        group.Slots.Add(bob);
        group.Slots.Add(carol);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var carolSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(carolSession);

        var bobCatchUpTask = manager.CatchUpSyncAsync(vm, bob);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's catch-up in flight

        var carolCatchUpTask = manager.CatchUpSyncAsync(vm, carol);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // carol not started yet

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await bobCatchUpTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Same(carolSession, factory.CreatedSessions[2]);

        carolSession.CompleteLoginSuccessfully(slot: 3);
        await carolCatchUpTask;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, carolSession.Socket.DisconnectCallCount);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task AddSlotMidSweep_QueuesOwnCatchUp_NotRace()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession); // part of the sweep's own sibling list, computed at sweep start

        // The sweep's sibling list is just [Bob]; Dave doesn't exist yet.
        var switchTask = manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's sweep catch-up in flight

        // Dave isn't in the sweep's precomputed list, so only this explicit call (queued on the gate) catches him up.
        var dave = new SlotProfile { GroupId = group.Id, SlotName = "Dave" };
        group.Slots.Add(dave);
        var daveSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(daveSession);

        var daveCatchUpTask = manager.CatchUpSyncAsync(vm, dave);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // dave queued, not raced in

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchTask;
        Dispatcher.UIThread.RunJobs();

        // Dave's connect started only once the sweep released the gate.
        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Same(daveSession, factory.CreatedSessions[2]);
        Assert.Equal(0, daveSession.Socket.DisconnectCallCount);

        daveSession.CompleteLoginSuccessfully(slot: 3);
        await daveCatchUpTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, daveSession.Socket.DisconnectCallCount);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task CatchUpSyncCoreAsync_NoOpsForSlotRemovedBeforeItsTurn_WhileStillWaitingInSweep()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob", "Carol");
        var alice = group.Slots[0];
        var carol = group.Slots[2];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        // Nothing enqueued for Carol: if her turn runs anyway, the default session's login never completes and the Timeout trips.

        var switchTask = manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's catch-up in flight; carol still waiting in the sweep's own list

        group.Slots.Remove(carol);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, factory.CreatedSessions.Count);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task CatchUpSyncCoreAsync_NoOpsForSlotRemovedBeforeItsTurn_ViaSeparateQueuedCall()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var otherSlot = new SlotProfile { GroupId = group.Id, SlotName = "Other" };
        group.Slots.Add(otherSlot);
        var otherSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(otherSession);
        var otherTask = manager.CatchUpSyncAsync(vm, otherSlot);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + other's catch-up in flight

        var bobTask = manager.CatchUpSyncAsync(vm, bob); // queues behind otherTask's gate
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // bob not started yet

        group.Slots.Remove(bob); // removed while its queued call is still waiting its turn

        otherSession.CompleteLoginSuccessfully(slot: 3);
        await otherTask;
        await bobTask; // no-ops immediately once its turn comes
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, factory.CreatedSessions.Count); // bob's queued call never created a session
    }
}
