using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;

namespace AvaloniaApplication1.Tests;

/// <summary>The watched slot's state on <see cref="GroupViewModel"/>: filter entry, room-slot flags and the strip.</summary>
public class WatchedSlotGroupViewModelTests
{
    private static GroupViewModel MakeGroup(string? watchedSlotName, params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Name = "Test Server", WatchedSlotName = watchedSlotName };
        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static WatchedItemRow Row(string itemName, bool isNew = true) =>
        new() { ItemName = itemName, SenderName = "Mira", LocationName = "Chest", IsNew = isNew };

    [AvaloniaFact]
    public void ForeignWatchedSlot_GetsFilterEntry_ButNeverJoinsChatAs()
    {
        var group = MakeGroup("malandor", "Alice");

        var entry = Assert.Single(group.SlotFilterOptions.OfType<SlotProfile>(), slot => slot.SlotName == "malandor");
        Assert.Equal(group.Group.WatchedSlotId, entry.Id);
        Assert.Equal("malandor (watched)", entry.DisplayName);
        Assert.DoesNotContain(group.Slots, slot => slot.SlotName == "malandor");
        Assert.DoesNotContain(group.Group.Slots, slot => slot.SlotName == "malandor");
    }

    [AvaloniaFact]
    public void ConfiguredWatchedSlot_KeepsItsOwnFilterEntry_WithoutAnExtraOne()
    {
        var group = MakeGroup("alice", "Alice");

        Assert.True(group.IsWatchedSlotOwn);
        Assert.Equal(2, group.SlotFilterOptions.Count); // "All slots" + Alice
    }

    [AvaloniaFact]
    public void FilterEntry_SurvivesTheLeaderReorder_AndStaysSelected()
    {
        var group = MakeGroup("malandor", "Alice", "Bob");
        var entry = group.SlotFilterOptions.OfType<SlotProfile>().Single(slot => slot.SlotName == "malandor");
        group.SelectedEventsSlotFilter = entry;

        group.SetLeaderStateWithoutTriggeringSwitch(group.Group.Slots[1].Id, group.Group.Slots[1]);

        Assert.Contains(entry, group.SlotFilterOptions);
        Assert.Same(entry, group.SelectedEventsSlotFilter);
    }

    [AvaloniaFact]
    public void ChangingTheWatchedSlot_ReplacesTheFilterEntry_AndClearsTheStrip()
    {
        var group = MakeGroup("malandor", "Alice");
        group.AddWatchedItem(Row("Hookshot"));

        group.Group.WatchedSlotName = "Mira";

        Assert.Empty(group.WatchedItems);
        Assert.Contains(group.SlotFilterOptions.OfType<SlotProfile>(), slot => slot.SlotName == "Mira");
        Assert.DoesNotContain(group.SlotFilterOptions.OfType<SlotProfile>(), slot => slot.SlotName == "malandor");
    }

    [AvaloniaFact]
    public void ApplyWatchedSlotStatuses_FlagsTheSlotAndEveryItemLinkGroupItBelongsTo()
    {
        var group = MakeGroup("malandor", "Alice");
        RoomPlayerRef P(int slot, string name) => new(slot, name, name);
        group.ApplyItemLinkGroups(
        [
            new(P(11, "Samus Link"), "Super Metroid", [P(3, "Mira"), P(12, "malandor")]),
            new(P(9, "Sword Link"), "A Link to the Past", [P(1, "Alice"), P(4, "Player One")]),
        ]);

        group.SetWatchedSlotNumber(12);
        group.ApplyWatchedSlotStatuses();

        Assert.True(group.GetRoomSlotStatus(12).IsWatched);
        Assert.True(group.GetRoomSlotStatus(11).IsWatched);
        Assert.False(group.GetRoomSlotStatus(9).IsWatched);
        Assert.False(group.GetRoomSlotStatus(3).IsWatched);

        group.SetWatchedSlotNumber(null);
        group.ApplyWatchedSlotStatuses();

        Assert.False(group.GetRoomSlotStatus(12).IsWatched);
        Assert.False(group.GetRoomSlotStatus(11).IsWatched);
    }

    [AvaloniaFact]
    public void WatchedSlotHasGoal_FollowsTheRoomStatus()
    {
        var group = MakeGroup("malandor");
        group.SetWatchedSlotNumber(12);
        group.ApplyWatchedSlotStatuses();

        group.ApplyGoals(null, [12]);

        Assert.True(group.WatchedSlotHasGoal);
    }

    [AvaloniaFact]
    public void AddWatchedItem_PutsNewestFirst_AndKeepsAtMostTheConfiguredCount()
    {
        var group = MakeGroup("malandor");
        group.Group.WatchedItemCount = 2;

        group.AddWatchedItem(Row("Morph Ball"));
        group.AddWatchedItem(Row("Varia Suit"));
        group.AddWatchedItem(Row("Space Jump"));

        Assert.Equal(new[] { "Space Jump", "Varia Suit" }, group.WatchedItems.Select(row => row.ItemName));
        Assert.Equal("malandor: Space Jump", group.WatchedOverviewText);
    }

    [AvaloniaFact]
    public void NewRows_RaiseNewWatchedItemsAdded_UntilMarkedSeen()
    {
        var group = MakeGroup("malandor");
        var raised = 0;
        group.NewWatchedItemsAdded += () => raised++;

        group.AddWatchedItem(Row("Morph Ball"));
        group.AddWatchedItem(Row("Bomb", isNew: false));

        Assert.Equal(1, raised);
        Assert.Equal(1, group.NewWatchedItemCount);

        group.MarkWatchedItemsSeen();

        Assert.Equal(0, group.NewWatchedItemCount);
        Assert.False(group.HasNewWatchedItems);
    }

    [AvaloniaFact]
    public void CollapsedStrip_ShowsTheLatestItemInsteadOfTheCaption()
    {
        var group = MakeGroup("malandor");
        group.FilterLayout.WatchedItemsExpanded = false;

        Assert.False(group.ShowWatchedLatestInHeader); // nothing to show yet
        Assert.True(group.ShowWatchedCaptionInHeader);

        group.AddWatchedItem(Row("Screw Attack"));

        Assert.True(group.ShowWatchedLatestInHeader);
        Assert.False(group.ShowWatchedCaptionInHeader);
        Assert.Equal("Screw Attack", group.LatestWatchedItem?.ItemName);

        group.ToggleWatchedItemsExpandedCommand.Execute(null);

        Assert.True(group.FilterLayout.WatchedItemsExpanded);
        Assert.False(group.ShowWatchedLatestInHeader);
    }
}
