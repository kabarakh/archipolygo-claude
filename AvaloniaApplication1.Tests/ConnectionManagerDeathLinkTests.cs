using System.Threading.Tasks;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): Feature-Plaene/Archiv/DeathLink.md -
/// <see cref="ConnectionManager"/> always enables DeathLink reception for the
/// leader session only (no per-group opt-in - see that plan's "Status"
/// section for why), and turns an incoming DeathLink into exactly one
/// <see cref="EventEntry"/> of the new <see cref="EventType.DeathLink"/> via a
/// real <see cref="MessageHistoryService"/> (a spy would only prove
/// <c>ConnectionManager</c> called a method, not that a real event actually
/// landed in the group's log the user sees).
/// </summary>
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

        // Alice's leader connect triggers the sibling catch-up sweep, which
        // briefly connects Bob too (see SwitchLeaderAsync) - but only Alice,
        // the leader, should ever get a DeathLink service.
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.True(factory.DeathLinkServicesBySession.ContainsKey(aliceSession));
        Assert.False(factory.DeathLinkServicesBySession.ContainsKey(bobSession));
    }
}
