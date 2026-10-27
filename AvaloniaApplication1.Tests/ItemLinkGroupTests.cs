using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>The "Item links" panel and the member tooltip on an item-link group's name.</summary>
public class ItemLinkGroupTests
{
    private static GroupViewModel MakeTabGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Carol" });
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static RoomPlayerRef P(int slot, string name) => new(slot, name, name);

    private static ItemLinkGroupSnapshot SwordLink() =>
        new(P(4, "Sword Link"), "Game", new[] { P(2, "Bob"), P(3, "Carol") });

    private static FakeArchipelagoSession MakeRoomSession()
    {
        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 0, "Server", null, "Archipelago", null, null),
            new PlayerInfo(0, 1, "Alice", null, "Game", null, null),
            new PlayerInfo(0, 2, "Bob", "Bobby", "Game", null, null),
            new PlayerInfo(0, 3, "Carol", null, "Game", null, null),
            new PlayerInfo(0, 4, "Sword Link", null, "Game", null, new[] { 2, 3 }),
        };
        return session;
    }

    // ── View model ──

    [Fact]
    public void ApplyItemLinkGroups_ListsOwnMembersFirst_AndSetsTheTooltipOnTheGroupsStatus()
    {
        var group = MakeTabGroup();

        group.ApplyItemLinkGroups(new[] { SwordLink() });

        var link = Assert.Single(group.ItemLinkGroups);
        Assert.True(group.HasItemLinkGroups);
        Assert.Same(group.GetRoomSlotStatus(4), link.Slot);
        Assert.Equal(new[] { "Carol", "Bob" }, link.Members.Select(m => m.DisplayName));
        Assert.Equal(EventTextSegmentKind.OwnSlotName, link.Members[0].Kind);
        Assert.Equal(EventTextSegmentKind.OtherSlotName, link.Members[1].Kind);
        Assert.Equal("Item link: Carol, Bob", group.GetRoomSlotStatus(4).ItemLinkMembersText);
        Assert.Null(group.GetRoomSlotStatus(2).ItemLinkMembersText);
    }

    [Fact]
    public void ApplyItemLinkGroups_Empty_ClearsTheTooltip_AndLeavesTheItemLinksPanel()
    {
        var group = MakeTabGroup();
        group.ApplyItemLinkGroups(new[] { SwordLink() });
        group.ShowItemLinksPanelCommand.Execute(null);

        group.ApplyItemLinkGroups(Enumerable.Empty<ItemLinkGroupSnapshot>());

        Assert.False(group.HasItemLinkGroups);
        Assert.Null(group.GetRoomSlotStatus(4).ItemLinkMembersText);
        Assert.Equal(RightPanelView.Hints, group.SelectedRightPanel);
    }

    [Fact]
    public void ItemLinksPanel_HidesTheHintsAndItemsFilterBar()
    {
        var group = MakeTabGroup();
        Assert.True(group.IsRightPanelFilterBarVisible);

        group.ShowItemLinksPanelCommand.Execute(null);

        Assert.False(group.IsRightPanelFilterBarVisible);
    }

    // ── Translator and ConnectionManager ──

    [AvaloniaFact]
    public void ReadItemLinkGroups_TakesOnlyGroupEntries_WithTheirMembersDisplayNames()
    {
        var group = MakeTabGroup();
        var translator = new SessionEventTranslator(new NoOpMessageHistoryService(), new NoOpHintService());

        translator.ReadItemLinkGroups(group, MakeRoomSession());
        Dispatcher.UIThread.RunJobs();

        var link = Assert.Single(group.ItemLinkGroups);
        Assert.Equal("Sword Link", link.DisplayName);
        Assert.Equal(new[] { "Carol", "Bob (Bobby)" }, link.Members.Select(m => m.DisplayName));
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_ReadsItemLinkGroups()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var session = MakeRoomSession();
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, group.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasItemLinkGroups);
        Assert.NotNull(vm.GetRoomSlotStatus(4).ItemLinkMembersText);
    }

    // ── View ──

    [AvaloniaFact]
    public void ItemLinksButton_AppearsOnlyWithItemLinkGroups_AndOpensThePanel()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Carol", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 1200, Height = 550 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        Button ItemLinksButton() => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Item links"));
        Assert.False(ItemLinksButton().IsVisible);

        group.ApplyItemLinkGroups(new[] { SwordLink() });
        Dispatcher.UIThread.RunJobs();
        Assert.True(ItemLinksButton().IsVisible);

        ItemLinksButton().Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Sword Link", texts);
        Assert.Contains("2 members", texts);
        Assert.Contains("Bob", texts);
    }
}
