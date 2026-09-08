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

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): the transient-retry path in
/// <c>ConnectSlotSessionAsync</c>, and <c>DisconnectGroupAsync</c>'s
/// AutoConnect/GroupPersistNeeded bookkeeping.
/// </summary>
public class ConnectionManagerRetryAndDisconnectTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task ConnectSlotSessionAsync_RetriesTransientFailures_UpToMax_ThenSucceeds()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        // Two attempts that fail exactly the way a transient hiccup does -
        // the underlying task itself faults, not a LoginFailure result (see
        // IsTransientConnectFailure) - then a third, brand-new session (a
        // fresh one per attempt, exactly like the real ConnectSlotSessionAsync
        // loop) that succeeds.
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

        // A genuine rejection comes back as a LoginFailure *result*, never
        // as a thrown/faulted task - see IsTransientConnectFailure's doc
        // comment on why that distinction is what actually decides whether
        // a failure gets retried.
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
