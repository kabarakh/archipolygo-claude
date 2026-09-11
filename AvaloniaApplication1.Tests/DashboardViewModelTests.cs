using System.Collections.ObjectModel;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="DashboardViewModel"/>'s
/// aggregation properties and its shared, server-spanning Hints overview
/// (<see cref="DashboardViewModel.VisibleHints"/>) - see
/// Feature-Plaene/Dashboard-Tab.md. No Avalonia bindings/dispatcher involved
/// (same reasoning as <see cref="GroupViewModelOrderingTests"/>), so a plain
/// <see cref="FakeConnectionManager"/> stand-in is enough.
/// </summary>
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

    // ── Aggregation properties ──

    [Fact]
    public void Aggregates_SumAcrossEveryConfiguredGroup()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { });

        var g1 = MakeGroup("Server1");
        var g2 = MakeGroup("Server2");
        groups.Add(g1);
        groups.Add(g2);

        g1.UnreadEventCount = 5;
        g2.UnreadEventCount = 7;
        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Shield"));
        g2.Hints.Add(MakeHint(Guid.NewGuid(), "Bow", found: true)); // found - still counts toward UnfoundHintCount denominator? No: UnfoundHintCount only counts unfound.

        Assert.Equal(2, dashboard.TotalServers);
        Assert.Equal(12, dashboard.TotalUnreadEvents);
        Assert.Equal(2, dashboard.TotalUnfoundHints); // Sword + Shield, not the found Bow
    }

    [Fact]
    public void Aggregates_ReactToGroupAddedOrRemoved()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { });

        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        g1.UnreadEventCount = 3;
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
        var dashboard = new DashboardViewModel(groups, _ => { });

        g1.UnreadEventCount = 4;
        Assert.Equal(4, dashboard.TotalUnreadEvents);

        g1.Hints.Add(MakeHint(Guid.NewGuid(), "Sword"));
        Assert.Equal(1, dashboard.TotalUnfoundHints);
    }

    // ── Shared Hints overview: VisibleHints ──

    [Fact]
    public void VisibleHints_NeverIncludesFoundHints_RegardlessOfOtherFilters()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

        dashboard.HintFilter.SelectedServer = g1;
        Assert.Equal(new SlotProfile?[] { null, alice }, dashboard.HintFilter.SlotOptions);

        dashboard.HintFilter.SelectedSlot = alice;
        dashboard.HintFilter.SelectedServer = g2;

        // Switching servers can't leave a slot selected that belongs to the old one.
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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, g => selected = g);

        dashboard.SelectGroupAndLeaveDashboard(g1);

        Assert.Equal(g1, selected);
    }

    // ── Shared Events view (dev follow-up, 2026-09-11) ──

    [Fact]
    public void SelectedLeftPanel_DefaultsToOverview()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { });

        Assert.Equal(DashboardLeftPanel.Overview, dashboard.SelectedLeftPanel);
    }

    [Fact]
    public void ShowEventsPanelAndShowOverviewPanelCommands_ToggleSelectedLeftPanel()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

        Assert.Equal(new GroupViewModel?[] { null, g1 }, dashboard.EventsFilter.ServerOptions);
    }

    /// <summary>
    /// The Events filter row's Slot dropdown is a pure display filter (dev
    /// decision 2026-09-12) - same copy-not-share/rebuild-on-server-change
    /// pattern as <see cref="HintSlotFilterOptions"/>, completely unrelated
    /// to <see cref="DashboardViewModel.SelectedSendServerGroup"/>.
    /// </summary>
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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

        g1.Events.Add(new EventEntry { Text = "Alice's event", Type = EventType.ItemReceived, SlotId = alice.Id });
        g1.Events.Add(new EventEntry { Text = "Bob's event", Type = EventType.ItemReceived, SlotId = bob.Id });
        g1.Events.Add(new EventEntry { Text = "room-wide chat", Type = EventType.Chat, SlotId = null });

        dashboard.EventsFilter.SelectedServer = g1;
        dashboard.EventsFilter.SelectedSlot = alice;

        Assert.Equal(
            new[] { "Alice's event", "room-wide chat" },
            dashboard.VisibleEvents.Select(r => r.Event.Text));
    }

    /// <summary>Picking a slot in the filter row must never switch the group's leader - unlike SelectedSendServerGroup, this is a passive filter.</summary>
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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });
        dashboard.EventsFilter.SelectedServer = g1;

        groups.Remove(g1);

        Assert.Null(dashboard.EventsFilter.SelectedServer);
        Assert.Equal(new GroupViewModel?[] { null }, dashboard.EventsFilter.ServerOptions);
    }

    /// <summary>
    /// The slot dropdown for this view is NOT a separate Dashboard-local
    /// filter (unlike the Hints slot dropdown) - it's the exact same
    /// <see cref="GroupViewModel.SelectedChatSlot"/>/<see cref="GroupViewModel.Slots"/>
    /// the tab's own "Chat as" dropdown uses, reached via a nested binding in
    /// DashboardView.axaml. This locks in that there's no separate
    /// Dashboard-only slot state to keep in sync at all.
    /// </summary>
    [Fact]
    public void SelectedEventsServerFilter_SlotsAndSelectedChatSlot_AreTheGroupsOwnLiveState()
    {
        var groups = new ObservableCollection<GroupViewModel>();
        var g1 = MakeGroup("Server1");
        groups.Add(g1);
        var alice = new SlotProfile { GroupId = g1.Group.Id, SlotName = "Alice" };
        g1.Group.Slots.Add(alice);
        g1.SetLeaderStateWithoutTriggeringSwitch(alice.Id, alice);
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

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
        var dashboard = new DashboardViewModel(groups, _ => { });

        dashboard.SelectedSendServerGroup = g1;
        Assert.True(dashboard.CanSendMessage);

        dashboard.SelectedSendServerGroup = g2;
        Assert.False(dashboard.CanSendMessage, "g2 has no connected leader.");
    }
}
