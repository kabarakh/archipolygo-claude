using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.TestSupport;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): <see cref="Archipolygo.Services.ConnectionManager"/>'s
/// <c>BuildSlotRoster</c> keeps <see cref="Archipolygo.Models.SlotProfile.Alias"/>
/// current as a side effect of matching the room roster - see that method's
/// doc comment for why it's the roster-matching code that owns this rather
/// than a separate pass.
/// </summary>
public class ConnectionManagerAliasTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_LearnsOwnAliasFromTheRoomRoster()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "KabaDone");
        var kabaDone = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 1, "KabaDone", "KabaHarkinian", "Some Game", null, null),
        };
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, kabaDone);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("KabaHarkinian", kabaDone.Alias);
        Assert.Equal("KabaDone (KabaHarkinian)", kabaDone.DisplayName);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_AlsoLearnsAliasForNonLeaderConfiguredSiblings()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        aliceSession.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 1, "Alice", "Alice", "Some Game", null, null), // no custom alias set
            new PlayerInfo(0, 2, "Bob", "Bobby", "Some Game", null, null),
        };
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        bobSession.CompleteLoginSuccessfully(slot: 2);

        // Alice's own leader connect triggers the sibling catch-up sweep,
        // during which BuildSlotRoster also runs for Bob (see
        // ConnectionManager.OnLeaderMessageReceived/TrackHintsForSiblingOnLeader,
        // which both call it against Alice's still-open leader session).
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Alice", alice.Alias); // no custom alias set - PlayerInfo.Alias defaults to Name
        Assert.Equal("Alice", alice.DisplayName); // alias == name -> no "(...)" suffix
        Assert.Equal("Bobby", bob.Alias);
        Assert.Equal("Bob (Bobby)", bob.DisplayName);
    }
}
