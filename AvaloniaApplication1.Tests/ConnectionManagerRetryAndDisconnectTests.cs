using System.Collections.Generic;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

public class ConnectionManagerRetryAndDisconnectTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task ConnectSlotSessionAsync_RetriesTransientFailures_UpToMax_ThenSucceeds()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        // Transient failures fault the task (not a LoginFailure result); each attempt gets a fresh session.
        var attempt1 = new FakeArchipelagoSession(numericSlot: 1);
        attempt1.LoginResultSource.SetException(new TaskCanceledException());
        var attempt2 = new FakeArchipelagoSession(numericSlot: 1);
        attempt2.LoginResultSource.SetException(new TaskCanceledException());
        var attempt3 = new FakeArchipelagoSession(numericSlot: 1);
        attempt3.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(attempt1);
        factory.Enqueue(attempt2);
        factory.Enqueue(attempt3);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        // 1 initial attempt + 2 retries (MaxTransientConnectRetries).
        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ConnectSlotSessionAsync_RealLoginRejection_IsNeverRetried()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        // A genuine rejection is a LoginFailure result, never a faulted task - so it's not retried.
        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.LoginResultSource.SetResult(new LoginFailure("bad password"));
        factory.Enqueue(session);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(factory.CreatedSessions); // never retried
        Assert.Null(vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Error, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task DisconnectGroupAsync_ClearsAutoConnect_AndRaisesGroupPersistNeeded()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.True(group.AutoConnect);

        var persistRaisedFor = new List<GroupViewModel>();
        manager.GroupPersistNeeded += g => persistRaisedFor.Add(g);

        await manager.DisconnectGroupAsync(vm);
        Dispatcher.UIThread.RunJobs();

        Assert.False(group.AutoConnect);
        Assert.Contains(vm, persistRaisedFor);
        Assert.Null(vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionState);
        Assert.Equal(1, session.Socket.DisconnectCallCount);
    }
}
