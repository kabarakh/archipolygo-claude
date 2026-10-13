using System.Collections.ObjectModel;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

public class DashboardViewModelTests
{
    private static GroupViewModel MakeGroup(string name)
    {
        var group = new ServerConnectionGroup { Name = name };
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static HintEntry MakeHint(Guid slotId, string itemName, bool found = false, EventTextSegmentKind itemKind = EventTextSegmentKind.ItemOther) =>
        new()
        {
            Key = Guid.NewGuid().ToString(),
            SlotId = slotId,
            ItemName = itemName,
            LocationName = "Somewhere",
            ReceivingPlayerName = "Receiver",
            FindingPlayerName = "Finder",
            Found = found,
            ItemKind = itemKind,
        };

    [Fact]
    public void Aggregates_SumAcrossEveryConfiguredGroup()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);

        g1.UnreadAttentionCount = 5;
        g2.UnreadAttentionCount = 7;
        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Shield"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Bow", found: true)); // found - not counted

        Assert.Equal(2, dashboard.TotalServers);
        Assert.Equal(12, dashboard.TotalUnreadEvents);
        Assert.Equal(2, dashboard.TotalUnfoundHints); // Sword + Shield, not the found Bow
    }

    [Fact]
    public void Aggregates_ReactToGroupAddedOrRemoved()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        g1.UnreadAttentionCount = 3;
        Assert.Equal(1, dashboard.TotalServers);
        Assert.Equal(3, dashboard.TotalUnreadEvents);

        groups.Remove(g1);
        Assert.Equal(0, dashboard.TotalServers);
        Assert.Equal(0, dashboard.TotalUnreadEvents);
    }

    [Fact]
    public void Aggregates_ReactToPropertyChangesOnAlreadyAddedGroup()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.UnreadAttentionCount = 4;
        Assert.Equal(4, dashboard.TotalUnreadEvents);

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        Assert.Equal(1, dashboard.TotalUnfoundHints);
    }

    [Fact]
    public void VisibleHints_NeverIncludesFoundHints_RegardlessOfOtherFilters()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        var slotId = Guid.NewGuid();
        g1.Hints.Add(MakeHint(slotId, "Open Hint"));
        g1.Hints.Add(MakeHint(slotId, "Found Hint", found: true));

        Assert.Single(dashboard.VisibleHints);
        Assert.Equal("Open Hint", dashboard.VisibleHints.Single().Hint.ItemName);
    }

    [Fact]
    public void VisibleHints_AllServers_ShowsEveryGroupsHintsMixed()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Shield"));

        Assert.Equal(2, dashboard.VisibleHints.Count());
        Assert.Contains(dashboard.VisibleHints, r => r.Group == g1 && r.Hint.ItemName == "Sword");
        Assert.Contains(dashboard.VisibleHints, r => r.Group == g2 && r.Hint.ItemName == "Shield");
    }

    [Fact]
    public void VisibleHints_SelectedServerFilter_ShowsOnlyThatGroupsHints()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Shield"));

        dashboard.HintFilter.SelectedServer = g1;

        Assert.Single(dashboard.VisibleHints);
        Assert.Equal("Sword", dashboard.VisibleHints.Single().Hint.ItemName);
    }

    [Fact]
    public void HintSlotFilterOptions_StaysJustNull_WhileAllServersSelected()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        g1.Group.Slots.Add(new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" });
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Equal(new SlotProfile?[] { null }, dashboard.HintFilter.SlotOptions);
    }

    [Fact]
    public void HintSlotFilterOptions_RepopulatedFromSelectedServer_AndResetOnServerChange()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        var bob = new SlotProfile { GroupId = g2.Group.Id, SlotName = "Bob" };
        g2.Group.Slots.Add(bob);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.HintFilter.SelectedServer = g1;
        Assert.Equal(new SlotProfile?[] { null, alice }, dashboard.HintFilter.SlotOptions);

        dashboard.HintFilter.SelectedSlot = alice;
        dashboard.HintFilter.SelectedServer = g2;

        Assert.Null(dashboard.HintFilter.SelectedSlot);
        Assert.Equal(new SlotProfile?[] { null, bob }, dashboard.HintFilter.SlotOptions);
    }

    [Fact]
    public void VisibleHints_SlotFilter_CombinesWithServerFilter()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        var bob = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Bob" };
        g1.Group.Slots.Add(alice);
        g1.Group.Slots.Add(bob);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Hints.Add(MakeHint(alice.Id, "Sword"));
        g1.Hints.Add(MakeHint(bob.Id, "Shield"));

        dashboard.HintFilter.SelectedServer = g1;
        dashboard.HintFilter.SelectedSlot = alice;

        Assert.Single(dashboard.VisibleHints);
        Assert.Equal("Sword", dashboard.VisibleHints.Single().Hint.ItemName);
    }

    [Fact]
    public void VisibleHints_ItemCategoryFilter_CombinesWithServerFilter()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Big Key", itemKind: EventTextSegmentKind.ItemProgression));
        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Rupees", itemKind: EventTextSegmentKind.ItemOther));

        dashboard.HintFilter.SelectedServer = g1;
        dashboard.SelectedHintItemCategoryFilter = ItemCategoryFilter.Progress;

        Assert.Single(dashboard.VisibleHints);
        Assert.Equal("Big Key", dashboard.VisibleHints.Single().Hint.ItemName);
    }

    [Fact]
    public void VisibleHints_UpdatesWhenANewHintArrives()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Empty(dashboard.VisibleHints);

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));

        Assert.Single(dashboard.VisibleHints);
    }

    [Fact]
    public void VisibleHints_HintTurningFound_RemovesItFromTheList()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        var hint = MakeHint(Guid.NewGuid(), "Sword");
        g1.Hints.Add(hint);
        Assert.Single(dashboard.VisibleHints);

        hint.Found = true;

        Assert.Empty(dashboard.VisibleHints);
    }

    [Fact]
    public void SelectGroupAndLeaveDashboard_InvokesCallbackWithClickedGroup()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);

        GroupViewModel? selected = null;
        var dashboard = new DashboardViewModel(groups, g => selected = g, (_, _, _) => { });

        dashboard.SelectGroupAndLeaveDashboard(g1);

        Assert.Equal(g1, selected);
    }

    [Fact]
    public void SelectedLeftPanel_DefaultsToOverview()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Equal(DashboardLeftPanel.Overview, dashboard.SelectedLeftPanel);
    }

    [Fact]
    public void ShowEventsPanelAndShowOverviewPanelCommands_ToggleSelectedLeftPanel()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.ShowEventsPanelCommand.Execute(null);
        Assert.Equal(DashboardLeftPanel.Events, dashboard.SelectedLeftPanel);

        dashboard.ShowOverviewPanelCommand.Execute(null);
        Assert.Equal(DashboardLeftPanel.Overview, dashboard.SelectedLeftPanel);
    }

    [Fact]
    public void VisibleEvents_AllServers_ShowsEveryGroupsEventsMixed()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "Alice connected", Type = EventType.Connected });
        g2.Events.Add(new EventEntry { Text = "hello", Type = EventType.Chat });

        Assert.Equal(2, dashboard.VisibleEvents.Count());
        Assert.Contains(dashboard.VisibleEvents, r => r.Group == g1 && r.Event.Text == "Alice connected");
        Assert.Contains(dashboard.VisibleEvents, r => r.Group == g2 && r.Event.Text == "hello");
    }

    [Fact]
    public void VisibleEvents_SelectedServerFilter_ShowsOnlyThatGroupsEvents()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "Alice connected", Type = EventType.Connected });
        g2.Events.Add(new EventEntry { Text = "hello", Type = EventType.Chat });

        dashboard.EventsFilter.SelectedServer = g1;

        Assert.Single(dashboard.VisibleEvents);
        Assert.Equal("Alice connected", dashboard.VisibleEvents.Single().Event.Text);
    }

    [Fact]
    public void VisibleEvents_UpdatesWhenANewEventArrives()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Empty(dashboard.VisibleEvents);

        g1.Events.Add(new EventEntry { Text = "hello", Type = EventType.Chat });

        Assert.Single(dashboard.VisibleEvents);
    }

    [Fact]
    public void EventsServerFilterOptions_LeadingNullEntry_ThenEveryConfiguredGroup()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Equal(new GroupViewModel?[] { null, g1 }, dashboard.EventsFilter.ServerOptions);
    }

    /// <summary>A pure display filter, unrelated to SelectedSendServerGroup.</summary>
    [Fact]
    public void EventsSlotFilterOptions_RepopulatedFromSelectedServer_AndResetOnServerChange()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        var bob = new SlotProfile { GroupId = g2.Group.Id, SlotName = "Bob" };
        g2.Group.Slots.Add(bob);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.Equal(new SlotProfile?[] { null }, dashboard.EventsFilter.SlotOptions);

        dashboard.EventsFilter.SelectedServer = g1;
        Assert.Equal(new SlotProfile?[] { null, alice }, dashboard.EventsFilter.SlotOptions);

        dashboard.EventsFilter.SelectedSlot = alice;
        dashboard.EventsFilter.SelectedServer = g2;

        Assert.Null(dashboard.EventsFilter.SelectedSlot);
        Assert.Equal(new SlotProfile?[] { null, bob }, dashboard.EventsFilter.SlotOptions);
    }

    [Fact]
    public void VisibleEvents_SlotFilter_CombinesWithServerFilter_RoomWideEntriesAlwaysPass()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        var bob = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Bob" };
        g1.Group.Slots.Add(alice);
        g1.Group.Slots.Add(bob);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "Alice's event", Type = EventType.ItemReceived, SlotId = alice.Id });
        g1.Events.Add(new EventEntry { Text = "Bob's event", Type = EventType.ItemReceived, SlotId = bob.Id });
        g1.Events.Add(new EventEntry { Text = "room-wide chat", Type = EventType.Chat, SlotId = null });

        dashboard.EventsFilter.SelectedServer = g1;
        dashboard.EventsFilter.SelectedSlot = alice;

        Assert.Equal(
            new[] { "Alice's event", "room-wide chat" },
            dashboard.VisibleEvents.Select(r => r.Event.Text));
    }

    [Fact]
    public void SelectedEventsSlotFilter_NeverTouchesTheLeader()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        var bob = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Bob" };
        g1.Group.Slots.Add(alice);
        g1.Group.Slots.Add(bob);
        g1.SetLeaderStateWithoutTriggeringSwitch(alice.Id, alice);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.EventsFilter.SelectedServer = g1;
        dashboard.EventsFilter.SelectedSlot = bob;

        Assert.Equal(alice, g1.SelectedChatSlot);
        Assert.Equal(alice.Id, g1.LeaderSlotId);
    }

    [Fact]
    public void SelectedEventsServerFilter_ResetToNull_WhenThatGroupIsRemoved()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });
        dashboard.EventsFilter.SelectedServer = g1;

        groups.Remove(g1);

        Assert.Null(dashboard.EventsFilter.SelectedServer);
        Assert.Equal(new GroupViewModel?[] { null }, dashboard.EventsFilter.ServerOptions);
    }

    /// <summary>No Dashboard-only slot state: the dropdown binds to the group's own SelectedChatSlot/Slots.</summary>
    [Fact]
    public void SelectedEventsServerFilter_SlotsAndSelectedChatSlot_AreTheGroupsOwnLiveState()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        g1.SetLeaderStateWithoutTriggeringSwitch(alice.Id, alice);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.EventsFilter.SelectedServer = g1;

        Assert.Same(g1.Slots, dashboard.EventsFilter.SelectedServer.Slots);
        Assert.Equal(alice, dashboard.EventsFilter.SelectedServer.SelectedChatSlot);
    }

    [Fact]
    public void CanSendMessage_FalseWithNoServerSelected_TrueOnceItsLeaderIsConnected()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        Assert.False(dashboard.CanSendMessage, "no server selected yet - must not report sendable.");

        dashboard.SelectedSendServerGroup = g1;
        Assert.False(dashboard.CanSendMessage, "server selected but not connected yet.");

        g1.SetLeaderStateWithoutTriggeringSwitch(alice.Id, alice);
        g1.ConnectionState = ConnectionState.Connected;

        Assert.True(dashboard.CanSendMessage);
    }

    [Fact]
    public void CanSendMessage_FollowsWhicheverServerIsCurrentlySelected()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        g1.SetLeaderStateWithoutTriggeringSwitch(alice.Id, alice);
        g1.ConnectionState = ConnectionState.Connected; // g1 is connected, g2 is not.
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.SelectedSendServerGroup = g1;
        Assert.True(dashboard.CanSendMessage);

        dashboard.SelectedSendServerGroup = g2;
        Assert.False(dashboard.CanSendMessage, "g2 has no connected leader.");
    }

    [Fact]
    public void VisibleEvents_RelevanceFilter_DefaultsToAll_ConcernsMeNarrowsToOwnSlotEntries()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "concerns me", Type = EventType.ItemReceived, ConcernsOwnSlot = true });
        g1.Events.Add(new EventEntry { Text = "other players' banter", Type = EventType.Chat, ConcernsOwnSlot = false });

        Assert.Equal(EventRelevanceFilter.All, dashboard.SelectedEventRelevanceFilter);
        Assert.Equal(2, dashboard.VisibleEvents.Count());

        dashboard.SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;

        Assert.Equal(new[] { "concerns me" }, dashboard.VisibleEvents.Select(r => r.Event.Text));
    }

    [Fact]
    public void VisibleEvents_CategoryFilter_NarrowsByEventType()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "a hint", Type = EventType.HintReceived });
        g1.Events.Add(new EventEntry { Text = "an item", Type = EventType.ItemReceived });
        g1.Events.Add(new EventEntry { Text = "chat", Type = EventType.Chat });

        dashboard.SelectedEventCategoryFilter = EventCategoryFilter.Hints;
        Assert.Equal(new[] { "a hint" }, dashboard.VisibleEvents.Select(r => r.Event.Text));

        dashboard.SelectedEventCategoryFilter = EventCategoryFilter.Items;
        Assert.Equal(new[] { "an item" }, dashboard.VisibleEvents.Select(r => r.Event.Text));

        dashboard.SelectedEventCategoryFilter = EventCategoryFilter.Chat;
        Assert.Equal(new[] { "chat" }, dashboard.VisibleEvents.Select(r => r.Event.Text));

        dashboard.SelectedEventCategoryFilter = EventCategoryFilter.All;
        Assert.Equal(3, dashboard.VisibleEvents.Count());
    }

    [Fact]
    public void VisibleEvents_ItemKindCheckboxes_DefaultToTrue_UncheckingOneHidesOnlyThatKind()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        g1.Events.Add(new EventEntry { Text = "progression item", Type = EventType.ItemReceived, Segments = new[] { new EventTextSegment("Big Key", EventTextSegmentKind.ItemProgression) } });
        g1.Events.Add(new EventEntry { Text = "trap item", Type = EventType.ItemReceived, Segments = new[] { new EventTextSegment("Bomb", EventTextSegmentKind.ItemTrap) } });
        g1.Events.Add(new EventEntry { Text = "connect", Type = EventType.Connected });

        Assert.True(dashboard.ShowProgressionItemEvents);
        Assert.True(dashboard.ShowUsefulItemEvents);
        Assert.True(dashboard.ShowFillerItemEvents);
        Assert.True(dashboard.ShowTrapItemEvents);
        Assert.Equal(3, dashboard.VisibleEvents.Count());

        dashboard.ShowTrapItemEvents = false;

        Assert.Equal(
            new[] { "progression item", "connect" },
            dashboard.VisibleEvents.Select(r => r.Event.Text));
    }

    [Fact]
    public void EventFilterCommands_SetTheExpectedFilterValues()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.ShowOwnEventsOnlyCommand.Execute(null);
        Assert.Equal(EventRelevanceFilter.ConcernsMe, dashboard.SelectedEventRelevanceFilter);

        dashboard.ShowAllEventsRelevanceCommand.Execute(null);
        Assert.Equal(EventRelevanceFilter.All, dashboard.SelectedEventRelevanceFilter);

        dashboard.ShowHintEventsOnlyCommand.Execute(null);
        Assert.Equal(EventCategoryFilter.Hints, dashboard.SelectedEventCategoryFilter);

        dashboard.ShowItemEventsOnlyCommand.Execute(null);
        Assert.Equal(EventCategoryFilter.Items, dashboard.SelectedEventCategoryFilter);

        dashboard.ShowChatEventsOnlyCommand.Execute(null);
        Assert.Equal(EventCategoryFilter.Chat, dashboard.SelectedEventCategoryFilter);

        dashboard.ShowAllEventCategoriesCommand.Execute(null);
        Assert.Equal(EventCategoryFilter.All, dashboard.SelectedEventCategoryFilter);
    }

    [Fact]
    public void EventFilters_AreIndependentFromAnyTabsOwnGroupViewModelFilters()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { }, (_, _, _) => { });

        dashboard.SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;
        dashboard.SelectedEventCategoryFilter = EventCategoryFilter.Chat;
        dashboard.ShowTrapItemEvents = false;

        Assert.Equal(EventRelevanceFilter.All, g1.SelectedEventRelevanceFilter);
        Assert.Equal(EventCategoryFilter.All, g1.SelectedEventCategoryFilter);
        Assert.True(g1.ShowTrapItemEvents);

        g1.SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;
        g1.SelectedEventCategoryFilter = EventCategoryFilter.Items;
        g1.ShowProgressionItemEvents = false;

        Assert.Equal(EventRelevanceFilter.ConcernsMe, dashboard.SelectedEventRelevanceFilter);
        Assert.Equal(EventCategoryFilter.Chat, dashboard.SelectedEventCategoryFilter);
        Assert.False(dashboard.ShowTrapItemEvents);
        Assert.True(dashboard.ShowProgressionItemEvents);
    }
}
