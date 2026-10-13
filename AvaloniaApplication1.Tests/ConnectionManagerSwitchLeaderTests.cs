using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

public class ConnectionManagerSwitchLeaderTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task ConnectsNewLeaderBeforeDisconnectingOld()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(alice.Id, vm.LeaderSlotId);

        // Added after Alice leads, so the switch below is same-group and triggers no sweep.
        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(bob);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);

        var switchTask = manager.SwitchLeaderAsync(vm, bob);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(alice.Id, vm.LeaderSlotId);
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(bob.Id, vm.LeaderSlotId);
        Assert.Equal(1, aliceSession.Socket.DisconnectCallCount);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task OnFirstConnectForGroup_RunsCatchUpForAllOtherSlots_OneAtATime()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob", "Carol");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var carolSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(carolSession);

        var switchTask = manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        // The sweep runs one slot at a time: Bob in flight, Carol not created yet.
        Assert.Equal(2, factory.CreatedSessions.Count);
        Assert.Same(bobSession, factory.CreatedSessions[1]);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, bobSession.Socket.DisconnectCallCount);
        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Same(carolSession, factory.CreatedSessions[2]);
        Assert.Equal(0, carolSession.Socket.DisconnectCallCount);

        carolSession.CompleteLoginSuccessfully(slot: 3);
        await switchTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, carolSession.Socket.DisconnectCallCount);
        Assert.Equal(0, aliceSession.Socket.DisconnectCallCount); // the leader itself stays open
        Assert.Equal(alice.Id, vm.LeaderSlotId);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SameGroupLeaderSwitch_DoesNotRerunCatchUpSweep()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobCatchUpSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobCatchUpSession);
        bobCatchUpSession.CompleteLoginSuccessfully(slot: 2);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's catch-up

        var bobLeaderSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobLeaderSession);
        bobLeaderSession.CompleteLoginSuccessfully(slot: 2);
        await manager.SwitchLeaderAsync(vm, bob);
        Dispatcher.UIThread.RunJobs();

        // No re-sweep: Alice stayed current via the outgoing leader's broadcast coverage.
        Assert.Equal(3, factory.CreatedSessions.Count); // alice + bob's catch-up + bob's leader connect
        Assert.Equal(bob.Id, vm.LeaderSlotId);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task Sweep_AbortsImmediately_WhenLeaderDropsMidPass()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob", "Carol");
        var alice = group.Slots[0];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var carolSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(carolSession);

        var switchTask = manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, factory.CreatedSessions.Count); // alice + bob's catch-up in flight

        aliceSession.Socket.RaiseSocketClosed();
        Dispatcher.UIThread.RunJobs();

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchTask;
        Dispatcher.UIThread.RunJobs();

        // The sweep aborted: no leader left to piggyback on.
        Assert.Equal(2, factory.CreatedSessions.Count);
        Assert.DoesNotContain(carolSession, factory.CreatedSessions);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SetsAutoConnectAndRaisesGroupPersistNeeded_OnSuccess()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        Assert.False(group.AutoConnect);

        var persistRaisedFor = new System.Collections.Generic.List<Archipolygo.ViewModels.GroupViewModel>();
        manager.GroupPersistNeeded += g => persistRaisedFor.Add(g);

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.True(group.AutoConnect);
        Assert.Equal(alice.Id, group.PreferredLeaderSlotId);
        Assert.Contains(vm, persistRaisedFor);
    }
}
