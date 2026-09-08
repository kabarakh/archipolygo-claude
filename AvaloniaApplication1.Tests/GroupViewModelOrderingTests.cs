using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="GroupViewModel.RefreshSlotOrder"/>'s
/// sort rule (leader first, then alphabetical). No Avalonia bindings/dispatcher
/// involved in this path (see <see cref="GroupViewModel"/>'s constructor and
/// <c>OnSlotsCollectionChanged</c>/<c>OnLeaderSlotIdChanged</c>), so a plain
/// <see cref="FakeConnectionManager"/> stand-in is enough - no real connection
/// is ever attempted here.
/// </summary>
public class GroupViewModelOrderingTests
{
    private static (GroupViewModel viewModel, ServerConnectionGroup group) MakeGroup(params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        var viewModel = new GroupViewModel(group, new FakeConnectionManager());

        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return (viewModel, group);
    }

    [Fact]
    public void RefreshSlotOrder_NoLeader_OrdersAlphabeticallyByName()
    {
        var (viewModel, _) = MakeGroup("Charlie", "alice", "Bob");

        Assert.Equal(new[] { "alice", "Bob", "Charlie" }, viewModel.Slots.Select(s => s.SlotName));
    }

    [Fact]
    public void RefreshSlotOrder_WithLeader_PutsLeaderFirstThenAlphabetical()
    {
        var (viewModel, group) = MakeGroup("Charlie", "Alice", "Bob");
        var leader = group.Slots.Single(s => s.SlotName == "Bob");

        viewModel.SetLeaderStateWithoutTriggeringSwitch(leader.Id, leader);

        Assert.Equal(new[] { "Bob", "Alice", "Charlie" }, viewModel.Slots.Select(s => s.SlotName));
    }

    [Fact]
    public void RefreshSlotOrder_LeaderChanges_MovesNewLeaderToFront()
    {
        var (viewModel, group) = MakeGroup("Alice", "Bob", "Charlie");
        var firstLeader = group.Slots.Single(s => s.SlotName == "Alice");
        viewModel.SetLeaderStateWithoutTriggeringSwitch(firstLeader.Id, firstLeader);
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, viewModel.Slots.Select(s => s.SlotName));

        var newLeader = group.Slots.Single(s => s.SlotName == "Charlie");
        viewModel.SetLeaderStateWithoutTriggeringSwitch(newLeader.Id, newLeader);

        Assert.Equal(new[] { "Charlie", "Alice", "Bob" }, viewModel.Slots.Select(s => s.SlotName));
    }

    [Fact]
    public void AddSlotsToGroup_InsertsNewSlotAtCorrectAlphabeticalPosition_LeaderStaysFirst()
    {
        var (viewModel, group) = MakeGroup("Alice", "Charlie");
        var leader = group.Slots.Single(s => s.SlotName == "Charlie");
        viewModel.SetLeaderStateWithoutTriggeringSwitch(leader.Id, leader);
        Assert.Equal(new[] { "Charlie", "Alice" }, viewModel.Slots.Select(s => s.SlotName));

        var newSlot = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        viewModel.AddSlotsToGroup(new[] { newSlot });

        // Leader (index 0) untouched; "Bob" inserted alphabetically after it.
        Assert.Equal(new[] { "Charlie", "Alice", "Bob" }, viewModel.Slots.Select(s => s.SlotName));
    }

    [Fact]
    public void RemoveSlotFromGroup_RemovesFromSlotsAndSlotFilterOptions_KeepsRestInOrder()
    {
        var (viewModel, group) = MakeGroup("Alice", "Bob", "Charlie");
        var bob = group.Slots.Single(s => s.SlotName == "Bob");

        viewModel.RemoveSlotFromGroup(bob);

        Assert.Equal(new[] { "Alice", "Charlie" }, viewModel.Slots.Select(s => s.SlotName));
        // SlotFilterOptions keeps its leading null ("All slots") entry plus the remaining slots.
        Assert.Equal(new string?[] { null, "Alice", "Charlie" }, viewModel.SlotFilterOptions.Select(s => s?.SlotName));
    }

    [Fact]
    public void RefreshSlotOrder_PreservesSelectedFilterSlot_AcrossLeaderChange()
    {
        var (viewModel, group) = MakeGroup("Alice", "Bob");
        var alice = group.Slots.Single(s => s.SlotName == "Alice");
        viewModel.SelectedEventsSlotFilter = alice;

        var bob = group.Slots.Single(s => s.SlotName == "Bob");
        viewModel.SetLeaderStateWithoutTriggeringSwitch(bob.Id, bob);

        // The rebuild in RefreshSlotOrder must not silently drop a still-valid selection.
        Assert.Equal(alice, viewModel.SelectedEventsSlotFilter);
    }
}
