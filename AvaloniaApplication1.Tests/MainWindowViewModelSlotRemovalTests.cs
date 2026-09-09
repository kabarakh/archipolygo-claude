using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A/B (Test-Umsetzungsplan.md): the exact scenario reported -
/// "Edit Server", mark a different slot as the new default leader, then
/// remove the *previous* (currently connected) leader, all before clicking
/// Save. Slot removal used to disconnect the group the instant "✕" was
/// clicked in the dialog (see <see cref="RemoveConfiguredSlotButtonTests"/>
/// for the dialog-side half of this fix); this file checks the other half -
/// that <see cref="MainWindowViewModel.UpdateGroup"/> (the "Save" moment)
/// is what actually applies a staged removal, not anything earlier.
/// </summary>
public class MainWindowViewModelSlotRemovalTests
{
    [Fact]
    public async Task RemovingTheCurrentLeader_OnlyDisconnectsAtSaveTime_NotBeforehand()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());

        // Alice becomes leader immediately (the only slot there is - see
        // MainWindowViewModel.AddNewGroup).
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var alice = group.Group.Slots.Single();

        // A second slot, Bob, added to the same already-connected server.
        var bob = new SlotProfile { GroupId = group.Group.Id, SlotName = "Bob" };
        mainWindowViewModel.AddSlotsToGroup(group, new[] { new StagedSlot { SlotName = "Bob", DisplayText = "Bob" } });
        bob = group.Group.Slots.Single(s => s.SlotName == "Bob");

        Assert.Equal(alice.Id, group.LeaderSlotId); // still connected as Alice

        // Simulates confirming "Edit Server" with: Bob marked as the new
        // default leader, Alice (the current live leader) staged for
        // removal - exactly what UpdateGroup is called with once Save is
        // clicked, per ConnectionEditorViewModel/ConnectionEditorResult.
        await mainWindowViewModel.UpdateGroup(
            group, group.Group.Name, group.Group.Host, group.Group.Port, group.Group.Password,
            autoConnect: false, preferredLeaderSlotId: bob.Id, slotsToRemove: new[] { alice });

        // Only now - as part of that one Save call - does removing the live
        // leader actually disconnect the group.
        Assert.Null(group.LeaderSlotId);
        Assert.Equal(ConnectionState.Disconnected, group.ConnectionState);

        // Alice is gone from the real configuration; Bob remains and is now
        // the configured default for next startup.
        Assert.DoesNotContain(alice, group.Group.Slots);
        Assert.Contains(bob, group.Group.Slots);
        Assert.Equal(bob.Id, group.Group.PreferredLeaderSlotId);
    }
}
