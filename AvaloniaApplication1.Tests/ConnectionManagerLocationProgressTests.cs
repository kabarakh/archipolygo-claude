using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): Tier 1 of Feature-Plaene/Fortschrittsanzeigen.md -
/// <see cref="Archipolygo.Services.ConnectionManager"/> populates
/// <see cref="Archipolygo.Models.SlotProfile.LocationsChecked"/>/<see cref="Archipolygo.Models.SlotProfile.LocationsTotal"/>
/// after a successful (fake) login, for both the leader and a catch-up
/// session, and keeps the leader's live via <c>CheckedLocationsUpdated</c>.
/// </summary>
public class ConnectionManagerLocationProgressTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_PopulatesLocationProgress()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Locations.AllLocations = new ReadOnlyCollection<long>(Enumerable.Range(1, 50).Select(i => (long)i).ToList());
        session.Locations.AllLocationsChecked = new ReadOnlyCollection<long>(new long[] { 1, 2, 3 });
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, alice.LocationsChecked);
        Assert.Equal(50, alice.LocationsTotal);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderSession_CheckedLocationsUpdated_KeepsProgressLive()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Locations.AllLocations = new ReadOnlyCollection<long>(Enumerable.Range(1, 10).Select(i => (long)i).ToList());
        session.Locations.AllLocationsChecked = new ReadOnlyCollection<long>(new long[] { 1 });
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, alice.LocationsChecked);

        // Simulate the server reporting more checks later, while the leader
        // session is still live (e.g. a remote !collect).
        session.Locations.AllLocationsChecked = new ReadOnlyCollection<long>(new long[] { 1, 2, 3, 4 });
        session.Locations.RaiseCheckedLocationsUpdated(session.Locations.AllLocationsChecked);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, alice.LocationsChecked);
        Assert.Equal(10, alice.LocationsTotal);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task CatchUpSession_AlsoPopulatesLocationProgress_ForSiblingSlot()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        bobSession.Locations.AllLocations = new ReadOnlyCollection<long>(Enumerable.Range(1, 20).Select(i => (long)i).ToList());
        bobSession.Locations.AllLocationsChecked = new ReadOnlyCollection<long>(new long[] { 5, 6 });
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        // Alice's own leader connect triggers the sibling catch-up sweep,
        // which is what actually connects Bob briefly (see SwitchLeaderAsync).
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, bob.LocationsChecked);
        Assert.Equal(20, bob.LocationsTotal);
    }
}
