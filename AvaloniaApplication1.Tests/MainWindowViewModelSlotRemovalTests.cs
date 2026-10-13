using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Reported scenario: make another slot the default leader and remove the live leader in one Edit Server session.
/// The removal (and disconnect) must only happen in UpdateGroup, i.e. on Save.
/// </summary>
public class MainWindowViewModelSlotRemovalTests
{
    [Fact]
    public async Task RemovingTheCurrentLeader_OnlyDisconnectsAtSaveTime_NotBeforehand()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());

        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var alice = group.Group.Slots.Single();

        var bob = new SlotProfile { GroupId = group.Group.Id, SlotName = "Bob" };
        mainWindowViewModel.AddSlotsToGroup(group, new[] { new StagedSlot { SlotName = "Bob", DisplayText = "Bob" } });
        bob = group.Group.Slots.Single(s => s.SlotName == "Bob");

        Assert.Equal(alice.Id, group.LeaderSlotId); // still connected as Alice

        // Save with Bob as new default leader and Alice (live leader) staged for removal.
        await mainWindowViewModel.UpdateGroup(
            group, group.Group.Name, group.Group.Host, group.Group.Port, group.Group.Password,
            autoConnect: false, preferredLeaderSlotId: bob.Id, slotsToRemove: new[] { alice });

        // Only now does removing the live leader disconnect the group.
        Assert.Null(group.LeaderSlotId);
        Assert.Equal(ConnectionState.Disconnected, group.ConnectionState);

        Assert.DoesNotContain(alice, group.Group.Slots);
        Assert.Contains(bob, group.Group.Slots);
        Assert.Equal(bob.Id, group.Group.PreferredLeaderSlotId);
    }
}
