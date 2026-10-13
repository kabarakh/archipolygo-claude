using System.Threading.Tasks;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>Uses a real MessageHistoryService so the test proves an event actually lands in the group's log.</summary>
public class ConnectionManagerDeathLinkTests
{
    private static ConnectionManager MakeManagerWithRealMessageHistory(out FakeSessionFactory factory)
    {
        factory = new FakeSessionFactory();
        var messageHistoryService = new MessageHistoryService(new FakePersistenceService(), new ProfileSyncStateStore(new FakePersistenceService()));
        return new ConnectionManager(
            messageHistoryService,
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: System.TimeSpan.Zero,
            transientConnectRetryDelay: System.TimeSpan.Zero);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_AlwaysEnablesDeathLink()
    {
        var manager = MakeManagerWithRealMessageHistory(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var deathLinkService = factory.DeathLinkServicesBySession[session];
        Assert.True(deathLinkService.IsEnabled);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task IncomingDeathLink_AppendsExactlyOneDeathLinkEventEntry()
    {
        var manager = MakeManagerWithRealMessageHistory(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var eventsBefore = vm.Events.Count;

        var deathLinkService = factory.DeathLinkServicesBySession[session];
        deathLinkService.RaiseDeathLinkReceived(new DeathLink("Bob", "Bob fell into lava."));
        Dispatcher.UIThread.RunJobs();

        var entry = Assert.Single(vm.Events, e => e.Type == EventType.DeathLink);
        Assert.Equal(eventsBefore + 1, vm.Events.Count);
        Assert.Contains("Bob fell into lava.", entry.Text);
        Assert.Null(entry.SlotId); // room-wide, not tied to any one configured slot
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task IncomingDeathLink_NullCause_FallsBackToSourcePlayerDied()
    {
        var manager = MakeManagerWithRealMessageHistory(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        var deathLinkService = factory.DeathLinkServicesBySession[session];
        deathLinkService.RaiseDeathLinkReceived(new DeathLink("Carol", null));
        Dispatcher.UIThread.RunJobs();

        var entry = Assert.Single(vm.Events, e => e.Type == EventType.DeathLink);
        Assert.Contains("Carol died.", entry.Text);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task CatchUpSession_NeverCreatesDeathLinkService()
    {
        var manager = MakeManagerWithRealMessageHistory(out var factory);
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        // The sibling sweep briefly connects Bob too, but only the leader gets a DeathLink service.
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.True(factory.DeathLinkServicesBySession.ContainsKey(aliceSession));
        Assert.False(factory.DeathLinkServicesBySession.ContainsKey(bobSession));
    }
}
