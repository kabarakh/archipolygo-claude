using System;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>The app-wide connect gate and catch-up queue: one handshake at a time across every server.</summary>
public class ConnectionManagerCatchUpQueueTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnectOnOtherServer_WaitsForInFlightCatchUpHandshake()
    {
        var (manager, factory) = MakeManager();
        var (vmA, groupA) = MakeGroup(manager, "Alice", "Bob");
        var (vmB, groupB) = MakeGroup(manager, "Dave");

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(aliceSession);
        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);

        await manager.SwitchLeaderAsync(vmA, groupA.Slots[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, bobSession.LoginCallCount); // Bob's catch-up handshake in flight

        var daveSession = new FakeArchipelagoSession(numericSlot: 1);
        daveSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(daveSession);

        var switchB = manager.SwitchLeaderAsync(vmB, groupB.Slots[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, daveSession.LoginCallCount);
        Assert.False(switchB.IsCompleted);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchB;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, daveSession.LoginCallCount);
        Assert.Equal(groupB.Slots[0].Id, vmB.LeaderSlotId);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task WaitingLeaderConnect_GoesBeforeEarlierQueuedCatchUp()
    {
        var (manager, factory) = MakeManager();
        var (vmA, groupA) = MakeGroup(manager, "Alice");
        var (vmB, groupB) = MakeGroup(manager, "Dave");
        var (vmC, groupC) = MakeGroup(manager, "Erin");

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(aliceSession);
        await manager.SwitchLeaderAsync(vmA, groupA.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        // Dave's leader handshake holds the gate.
        var daveSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(daveSession);
        var switchB = manager.SwitchLeaderAsync(vmB, groupB.Slots[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, daveSession.LoginCallCount);

        // Bob's catch-up queues first, Erin's leader connect after it.
        var bob = new SlotProfile { GroupId = groupA.Id, SlotName = "Bob" };
        groupA.Slots.Add(bob);
        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var bobCatchUp = manager.CatchUpSyncAsync(vmA, bob);
        Dispatcher.UIThread.RunJobs();

        var erinSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(erinSession);
        var switchC = manager.SwitchLeaderAsync(vmC, groupC.Slots[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, bobSession.LoginCallCount);
        Assert.Equal(0, erinSession.LoginCallCount);

        // Dave done: the gate goes to Erin, Bob keeps waiting.
        daveSession.CompleteLoginSuccessfully(slot: 1);
        await switchB;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, erinSession.LoginCallCount);
        Assert.Equal(0, bobSession.LoginCallCount);

        erinSession.CompleteLoginSuccessfully(slot: 1);
        await switchC;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(groupC.Slots[0].Id, vmC.LeaderSlotId);
        Assert.Equal(1, bobSession.LoginCallCount);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await bobCatchUp;
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SiblingsOfSeveralServers_ShareOneQueue_AndOneProgressTotal()
    {
        var (manager, factory) = MakeManager();
        var (vmA, groupA) = MakeGroup(manager, "Alice", "Bob");
        var (vmB, groupB) = MakeGroup(manager, "Carol", "Dave");

        var announced = 0;
        var completed = 0;
        manager.SlotSyncBatchStarting += count => announced += count;
        manager.SlotInitialSyncCompleted += (_, _) => completed++;

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(aliceSession);
        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);

        await manager.SwitchLeaderAsync(vmA, groupA.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        var carolSession = new FakeArchipelagoSession(numericSlot: 1);
        carolSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(carolSession);
        var daveSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(daveSession);

        var switchB = manager.SwitchLeaderAsync(vmB, groupB.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await switchB;
        Dispatcher.UIThread.RunJobs();

        // Every leader and sibling of both servers is one running total.
        Assert.Equal(4, announced);
        Assert.Equal(3, completed); // Dave still in flight
        Assert.Equal(1, daveSession.LoginCallCount);

        daveSession.CompleteLoginSuccessfully(slot: 2);
        await manager.WhenCatchUpQueueIdleAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, completed);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task OpenPasswordPrompt_DoesNotBlockOtherServersConnect()
    {
        var (manager, factory) = MakeManager();
        var (vmA, groupA) = MakeGroup(manager, "Alice");
        var (vmB, groupB) = MakeGroup(manager, "Dave");
        groupA.Slots[0].RequiresPassword = true;

        var prompt = new TaskCompletionSource<bool>();
        manager.PasswordRequested = (_, _, _, _) => prompt.Task;

        factory.Enqueue(new FakeArchipelagoSession(numericSlot: 1));
        var switchA = manager.SwitchLeaderAsync(vmA, groupA.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        var daveSession = new FakeArchipelagoSession(numericSlot: 1);
        daveSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(daveSession);
        await manager.SwitchLeaderAsync(vmB, groupB.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(groupB.Slots[0].Id, vmB.LeaderSlotId);
        Assert.False(switchA.IsCompleted);

        prompt.SetResult(false);
        await switchA;
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task Disconnect_DropsTheGroupsQueuedCatchUps_AndCountsThemDone()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob", "Carol");

        var announced = 0;
        var completed = 0;
        manager.SlotSyncBatchStarting += count => announced += count;
        manager.SlotInitialSyncCompleted += (_, _) => completed++;

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(aliceSession);
        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);

        await manager.SwitchLeaderAsync(vm, group.Slots[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, announced);

        var disconnect = manager.DisconnectGroupAsync(vm);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, completed); // Alice, plus Carol dropped from the queue; Bob still in flight

        bobSession.CompleteLoginSuccessfully(slot: 2);
        await disconnect;
        await manager.WhenCatchUpQueueIdleAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, completed);
        Assert.Equal(2, factory.CreatedSessions.Count); // Alice + Bob; Carol never connected
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectGate_HungHolder_GivesUpTheGateAfterMaxHold()
    {
        var gate = new ConnectGate(TimeSpan.FromMilliseconds(50));

        var hungLease = await gate.EnterAsync(isBackground: false, CancellationToken.None);
        Assert.NotNull(hungLease); // never disposed

        using var nextLease = await gate.EnterAsync(isBackground: true, CancellationToken.None);
        Assert.NotNull(nextLease);
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectGate_CancelledWaiter_ReturnsNull_AndDoesNotTakeTheGate()
    {
        var gate = new ConnectGate(TimeSpan.FromMinutes(1));
        var firstLease = await gate.EnterAsync(isBackground: false, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var cancelledWaiter = gate.EnterAsync(isBackground: true, cts.Token);
        var laterWaiter = gate.EnterAsync(isBackground: true, CancellationToken.None);
        cts.Cancel();

        Assert.Null(await cancelledWaiter);

        firstLease!.Dispose();
        using var laterLease = await laterWaiter;
        Assert.NotNull(laterLease);
    }
}
