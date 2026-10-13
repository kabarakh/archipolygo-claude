using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

public class DashboardTabTests
{
    private static MainWindow ShowWindow(MainWindowViewModel viewModel)
    {
        // Wide enough for the whole toolbar: a horizontal StackPanel doesn't wrap, and clicks past the window edge never land.
        var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Press+release on any control, e.g. an Overview row's Border that has no Command to invoke.</summary>
    private static void ClickButton(MainWindow window, Control control)
    {
        var localCenter = new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var pointInWindow = control.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Dashboard_IsShownByDefaultOnStartup()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);

        Assert.True(mainWindowViewModel.IsDashboardVisible);
        Assert.False(window.GetVisualDescendants().OfType<TabControl>().Single().IsEffectivelyVisible);
        Assert.True(window.GetVisualDescendants().OfType<DashboardView>().Single().IsEffectivelyVisible);

        Assert.Single(window.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "Tab View")));
        Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "Dashboard")));
    }

    [AvaloniaFact]
    public void ToggleButton_SwitchesBetweenTabControlAndDashboardView_AndBack()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);

        var tabControl = window.GetVisualDescendants().OfType<TabControl>().Single();
        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();

        var backButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Tab View"));
        ClickButton(window, backButton);

        Assert.True(tabControl.IsEffectivelyVisible);
        Assert.False(dashboardView.IsEffectivelyVisible);
        Assert.False(mainWindowViewModel.IsDashboardVisible);

        var toggleButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Dashboard"));
        ClickButton(window, toggleButton);

        Assert.False(tabControl.IsEffectivelyVisible);
        Assert.True(dashboardView.IsEffectivelyVisible);
        Assert.True(mainWindowViewModel.IsDashboardVisible);
    }

    [AvaloniaFact]
    public void Dashboard_OverviewAndHintsColumns_BothVisibleSimultaneously_NoInternalToggle()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var overviewList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "OverviewListBox");
        var hintsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardHintsListBox");

        Assert.True(overviewList.IsEffectivelyVisible);
        Assert.True(hintsList.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void OverviewRowClick_SelectsThatGroup_AndLeavesDashboard()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group2 = mainWindowViewModel.Groups[1];

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();

        // A real click, not SelectedItem: the row navigates via its own pointer handlers, not ListBox selection.
        ClickButton(window, FindOverviewRow(dashboardView, group2));

        Assert.Equal(group2, mainWindowViewModel.SelectedGroup);
        Assert.False(mainWindowViewModel.IsDashboardVisible);
    }

    [AvaloniaFact]
    public void HintRowClick_NavigatesToTheRowsOwnServer_EvenWhileAllServersSelected()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];

        group1.Hints.Add(new HintEntry { Key = "h1", SlotId = group1.Group.Slots[0].Id, ItemName = "Sword", LocationName = "Loc", ReceivingPlayerName = "Alice", FindingPlayerName = "Alice" });
        group2.Hints.Add(new HintEntry { Key = "h2", SlotId = group2.Group.Slots[0].Id, ItemName = "Shield", LocationName = "Loc", ReceivingPlayerName = "Bob", FindingPlayerName = "Bob" });

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        Assert.Null(mainWindowViewModel.Dashboard.HintFilter.SelectedServer); // "All servers" - the default.

        var hintsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardHintsListBox");
        var shieldRow = mainWindowViewModel.Dashboard.VisibleHints.Single(r => r.Hint.ItemName == "Shield");

        hintsList.SelectedItem = shieldRow;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(group2, mainWindowViewModel.SelectedGroup);
        Assert.False(mainWindowViewModel.IsDashboardVisible);
    }

    [AvaloniaFact]
    public void SummaryHeader_ReflectsAggregatesAcrossServers()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        group1.UnreadAttentionCount = 3;
        group1.Hints.Add(new HintEntry { Key = "h1", SlotId = group1.Group.Slots[0].Id, ItemName = "Sword", LocationName = "Loc", ReceivingPlayerName = "Alice", FindingPlayerName = "Alice" });

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        var summaryText = dashboardView.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == mainWindowViewModel.Dashboard.SummaryText);

        Assert.Contains("1 servers", summaryText.Text);
        Assert.Contains("3 unread events", summaryText.Text);
        Assert.Contains("1 open hints", summaryText.Text);
    }

    /// <summary>No single "current" server on the Dashboard; "Add server..." and "Disconnect all" don't need one, so they stay.</summary>
    [AvaloniaFact]
    public void GroupScopedToolbarButtons_HiddenWhileDashboardVisible_ShownOtherwise()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);

        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        Button Find(string content) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));

        var addSlot = Find("Add slot...");
        var editServer = Find("Edit server...");
        var removeServer = Find("Remove server");
        var addServer = Find("Add server...");
        var disconnectAll = Find("Disconnect all");

        Assert.True(addSlot.IsEffectivelyVisible);
        Assert.True(editServer.IsEffectivelyVisible);
        Assert.True(removeServer.IsEffectivelyVisible);

        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(addSlot.IsEffectivelyVisible);
        Assert.False(editServer.IsEffectivelyVisible);
        Assert.False(removeServer.IsEffectivelyVisible);
        Assert.True(addServer.IsEffectivelyVisible);
        Assert.True(disconnectAll.IsEffectivelyVisible);

        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(addSlot.IsEffectivelyVisible);
        Assert.True(editServer.IsEffectivelyVisible);
        Assert.True(removeServer.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void DashboardToggleButton_IsFirstInToolbar()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        var window = ShowWindow(mainWindowViewModel);

        var toolbar = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Orientation == Avalonia.Layout.Orientation.Horizontal);
        var children = toolbar.Children;

        var toggleButton = Assert.IsType<Button>(children.OfType<Button>().First());
        Assert.Equal("Tab View", toggleButton.Content);

        var toggleIndex = children.IndexOf(toggleButton);
        Assert.True(children[toggleIndex + 1] is Border, "expected a divider Border directly after the Dashboard toggle button.");
    }

    /// <summary>Tag-based lookup, see DashboardView.axaml.</summary>
    private static Button FindRowIcon(DashboardView dashboardView, GroupViewModel group, string tag) =>
        dashboardView.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("icon-button") && b.DataContext == group && (string?)b.Tag == tag);

    /// <summary>Tag-based lookup: several Borders in the row bind to the same GroupViewModel.</summary>
    private static Border FindOverviewRow(DashboardView dashboardView, GroupViewModel group) =>
        dashboardView.GetVisualDescendants().OfType<Border>()
            .Single(b => (string?)b.Tag == "OverviewRow" && b.DataContext == group);

    private static (MainWindow window, DashboardView dashboardView, MainWindowViewModel viewModel, GroupViewModel group) SetUpSingleGroupDashboard()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        return (window, dashboardView, mainWindowViewModel, group);
    }

    [AvaloniaFact]
    public void RowIcon_ConnectDisconnect_TogglesConnectionState_AndSwapsWhichIconShows()
    {
        var (window, dashboardView, _, group) = SetUpSingleGroupDashboard();

        Assert.Equal(ConnectionState.Connected, group.ConnectionState);
        Assert.True(FindRowIcon(dashboardView, group, "Disconnect").IsEffectivelyVisible);
        Assert.False(FindRowIcon(dashboardView, group, "Connect").IsEffectivelyVisible);

        ClickButton(window, FindRowIcon(dashboardView, group, "Disconnect"));

        Assert.Equal(ConnectionState.Disconnected, group.ConnectionState);
        Assert.True(FindRowIcon(dashboardView, group, "Connect").IsEffectivelyVisible);
        Assert.False(FindRowIcon(dashboardView, group, "Disconnect").IsEffectivelyVisible);

        ClickButton(window, FindRowIcon(dashboardView, group, "Connect"));

        Assert.Equal(ConnectionState.Connected, group.ConnectionState);
    }

    [AvaloniaFact]
    public void RowIcon_AddSlot_EnabledOnlyWhileConnected()
    {
        var (window, dashboardView, _, group) = SetUpSingleGroupDashboard();

        var addSlotIcon = FindRowIcon(dashboardView, group, "AddSlot");
        Assert.True(addSlotIcon.IsVisible);
        Assert.True(addSlotIcon.IsEnabled);

        ClickButton(window, FindRowIcon(dashboardView, group, "Disconnect"));

        Assert.True(addSlotIcon.IsVisible, "Add slot must stay visible while disconnected, only disabled.");
        Assert.False(addSlotIcon.IsEnabled);
    }

    /// <summary>The dialogs can't be driven headlessly, so this pins the handoff MainWindow relies on.</summary>
    [AvaloniaFact]
    public void RowIcon_AddSlotAndEditServer_RaiseTheirDashboardViewEvents_WithTheClickedRowsGroup()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();

        GroupViewModel? addSlotGroup = null;
        GroupViewModel? editServerGroup = null;
        dashboardView.AddSlotRequested += (_, g) => addSlotGroup = g;
        dashboardView.EditServerRequested += (_, g) => editServerGroup = g;

        ClickButton(window, FindRowIcon(dashboardView, group2, "AddSlot"));
        Assert.Equal(group2, addSlotGroup);

        ClickButton(window, FindRowIcon(dashboardView, group1, "EditServer"));
        Assert.Equal(group1, editServerGroup);
    }

    /// <summary>No confirmation dialog is wired up in tests, so RemoveGroupAsync removes immediately.</summary>
    [AvaloniaFact]
    public void RowIcon_RemoveServer_ActuallyRemovesTheClickedRowsGroup_LeavesOthersAndDashboardOpen()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];
        mainWindowViewModel.SelectedGroup = group1;

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        ClickButton(window, FindRowIcon(dashboardView, group2, "RemoveServer"));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(group2, mainWindowViewModel.Groups);
        Assert.Contains(group1, mainWindowViewModel.Groups);
        Assert.Equal(group1, mainWindowViewModel.SelectedGroup);
        Assert.True(mainWindowViewModel.IsDashboardVisible);
    }

    /// <summary>Icon clicks must not also trigger the row's navigate-to-tab click.</summary>
    [AvaloniaFact]
    public void RowIconClick_DoesNotAlsoNavigateAwayFromTheDashboard()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        var overviewList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "OverviewListBox");

        ClickButton(window, FindRowIcon(dashboardView, group, "EditServer"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsDashboardVisible, "clicking a row icon must not leave the Dashboard.");
        Assert.Null(overviewList.SelectedItem);
    }

    [AvaloniaFact]
    public void LeftPanelToggle_SwitchesOverviewAndEventsVisibility_HintsColumnStaysVisibleThroughout()
    {
        var (window, dashboardView, _, _) = SetUpSingleGroupDashboard();

        var overviewList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "OverviewListBox");
        var eventsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardEventsListBox");
        var hintsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardHintsListBox");

        Assert.True(overviewList.IsEffectivelyVisible, "Overview is the default left panel.");
        Assert.False(eventsList.IsEffectivelyVisible);
        Assert.True(hintsList.IsEffectivelyVisible);

        var eventsToggle = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events"));
        ClickButton(window, eventsToggle);

        Assert.False(overviewList.IsEffectivelyVisible);
        Assert.True(eventsList.IsEffectivelyVisible);
        Assert.True(hintsList.IsEffectivelyVisible, "Hints must stay visible regardless of the left panel toggle.");

        var overviewToggle = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Overview"));
        ClickButton(window, overviewToggle);

        Assert.True(overviewList.IsEffectivelyVisible);
        Assert.False(eventsList.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void EventsPanel_JumpToNewestButton_AppearsWhenScrolledAway_AndJumpsBackOnClick()
    {
        var (window, dashboardView, _, group) = SetUpSingleGroupDashboard();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));

        for (var i = 0; i < 150; i++)
        {
            group.Events.Add(new EventEntry { Text = $"Event {i}", Type = EventType.Chat });
        }
        Dispatcher.UIThread.RunJobs();

        var eventsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardEventsListBox");
        var scrollViewer = eventsList.GetVisualDescendants().OfType<ScrollViewer>().First();
        var jumpButton = dashboardView.GetVisualDescendants().OfType<JumpToNewestButton>().Single();

        Assert.False(jumpButton.IsEffectivelyVisible, "hidden while still following the newest event.");

        // Real wheel gesture - the only thing that disengages auto-follow.
        var localCenter = new Point(eventsList.Bounds.Width / 2, eventsList.Bounds.Height / 2);
        var pointOverList = eventsList.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseWheel(pointOverList, new Vector(0, 3), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(jumpButton.IsEffectivelyVisible, "expected the button to appear once scrolled away from the bottom.");

        ClickButton(window, jumpButton);

        Assert.False(jumpButton.IsEffectivelyVisible, "expected the button to hide itself again after jumping back to the bottom.");
        var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        Assert.True(distanceFromBottom < 1.0, $"expected the list back at the bottom; distance from bottom={distanceFromBottom}");
    }

    [AvaloniaFact]
    public void SendRow_SlotDropdownSelection_SwitchesTheServersLeader()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var bob = new SlotProfile { GroupId = group.Group.Id, SlotName = "Bob" };
        group.AddSlotsToGroup(new[] { bob });

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));

        mainWindowViewModel.Dashboard.SelectedSendServerGroup = group;
        Dispatcher.UIThread.RunJobs();

        var sendSlotComboBox = dashboardView.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "DashboardSendSlotComboBox");

        Assert.NotEqual(bob, group.SelectedChatSlot); // Alice (the first slot) is the leader so far.

        sendSlotComboBox.SelectedItem = bob;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(bob, group.SelectedChatSlot);
        Assert.Equal(bob.Id, group.LeaderSlotId);
    }

    [AvaloniaFact]
    public void EventsFilterRow_SlotDropdownSelection_NarrowsVisibleEvents_NeverSwitchesTheLeader()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var bob = new SlotProfile { GroupId = group.Group.Id, SlotName = "Bob" };
        group.AddSlotsToGroup(new[] { bob });
        group.Events.Add(new EventEntry { Text = "from Alice", Type = EventType.Chat, SlotId = group.Group.Slots[0].Id });
        group.Events.Add(new EventEntry { Text = "from Bob", Type = EventType.Chat, SlotId = bob.Id });

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        mainWindowViewModel.Dashboard.EventsFilter.SelectedServer = group;
        Dispatcher.UIThread.RunJobs();

        var filterSlotComboBox = dashboardView.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "DashboardEventsSlotComboBox");
        var leaderBeforeFiltering = group.SelectedChatSlot;

        filterSlotComboBox.SelectedItem = bob;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "from Bob" }, mainWindowViewModel.Dashboard.VisibleEvents.Select(r => r.Event.Text));
        Assert.Equal(leaderBeforeFiltering, group.SelectedChatSlot); // unchanged - this dropdown must never switch the leader.
    }

    [AvaloniaFact]
    public void SendRow_SlotAndSendControls_DisabledUntilASendServerIsPicked()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));

        var sendSlotComboBox = dashboardView.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "DashboardSendSlotComboBox");
        var sendButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Send"));

        Assert.False(sendSlotComboBox.IsEnabled, "no send server picked yet - the slot/leader dropdown must stay disabled.");
        Assert.False(sendButton.IsEnabled);

        viewModel.Dashboard.SelectedSendServerGroup = group;
        Dispatcher.UIThread.RunJobs();

        Assert.True(sendSlotComboBox.IsEnabled);
        Assert.True(sendButton.IsEnabled);
    }

    [AvaloniaFact]
    public void SendRow_SendButtonClick_SendsThroughTheSelectedSendServer()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        viewModel.Dashboard.SelectedSendServerGroup = group;
        Dispatcher.UIThread.RunJobs();

        group.MessageToSend = "hello room";
        Dispatcher.UIThread.RunJobs();

        var sendButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Send"));
        ClickButton(window, sendButton);

        // The fake records nothing; MessageToSend being cleared is the only synchronous signal the click reached this group.
        Assert.Equal(string.Empty, group.MessageToSend);
    }

    [AvaloniaFact]
    public void EventsFilterAndSendServer_AreIndependentSelections()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];

        var window = ShowWindow(mainWindowViewModel);
        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().Single();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));

        var filterServerComboBox = dashboardView.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "DashboardEventsServerComboBox");
        var sendServerComboBox = dashboardView.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "DashboardSendServerComboBox");

        filterServerComboBox.SelectedItem = group1;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(sendServerComboBox.SelectedItem);

        sendServerComboBox.SelectedItem = group2;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(group1, filterServerComboBox.SelectedItem);
    }

    [AvaloniaFact]
    public void EventsPanel_VisibleEvents_ShowsEventsFromTheSelectedGroup()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        group.Events.Add(new EventEntry { Text = "hello from Alice", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        Dispatcher.UIThread.RunJobs();

        var eventsList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "DashboardEventsListBox");
        var texts = eventsList.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        Assert.Contains(group.HeaderText, texts);
        Assert.Contains("hello from Alice", texts);
    }

    [AvaloniaFact]
    public void EventsPanel_RelevanceAndCategoryButtons_ClickSetsActiveClassAndFiltersTheList()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        group.Events.Add(new EventEntry { Text = "concerns me", Type = EventType.ItemReceived, ConcernsOwnSlot = true });
        group.Events.Add(new EventEntry { Text = "others chatting", Type = EventType.Chat, ConcernsOwnSlot = false });
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        Dispatcher.UIThread.RunJobs();

        var concernsMeButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Concerns me"));
        Assert.DoesNotContain("active", concernsMeButton.Classes);

        ClickButton(window, concernsMeButton);

        Assert.Contains("active", concernsMeButton.Classes);
        Assert.Equal(EventRelevanceFilter.ConcernsMe, viewModel.Dashboard.SelectedEventRelevanceFilter);
        Assert.Equal(new[] { "concerns me" }, viewModel.Dashboard.VisibleEvents.Select(r => r.Event.Text));

        var chatButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Chat"));
        ClickButton(window, chatButton);

        Assert.Contains("active", chatButton.Classes);
        Assert.Equal(EventCategoryFilter.Chat, viewModel.Dashboard.SelectedEventCategoryFilter);
    }

    [AvaloniaFact]
    public void EventsPanel_ItemKindCheckboxes_UncheckingHidesThatKindFromTheList()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        group.Events.Add(new EventEntry { Text = "trap item", Type = EventType.ItemReceived, Segments = new[] { new EventTextSegment("Bomb", EventTextSegmentKind.ItemTrap) } });
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        Dispatcher.UIThread.RunJobs();

        // The checkboxes live in a flyout; open it like a user would.
        var classesButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClassesButton");
        ClickButton(window, classesButton);
        Dispatcher.UIThread.RunJobs();
        var trapCheckbox = ((StackPanel)((Flyout)classesButton.Flyout!).Content!).Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Trap"));
        Assert.True(trapCheckbox.IsChecked);
        Assert.Single(viewModel.Dashboard.VisibleEvents);

        trapCheckbox.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Dashboard.ShowTrapItemEvents);
        Assert.Empty(viewModel.Dashboard.VisibleEvents);
        Assert.Equal("Classes 3/4", viewModel.Dashboard.EventItemClassFilterButtonText);
        Assert.Contains("active", classesButton.Classes);
    }

    [AvaloniaFact]
    public void EventsPanel_FilterButtons_DoNotAffectAnyTabsOwnGroupViewModelFilters()
    {
        var (window, dashboardView, viewModel, group) = SetUpSingleGroupDashboard();
        ClickButton(window, dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Events")));
        Dispatcher.UIThread.RunJobs();

        var concernsMeButton = dashboardView.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Concerns me"));
        ClickButton(window, concernsMeButton);

        Assert.Equal(EventRelevanceFilter.All, group.SelectedEventRelevanceFilter);
    }
}
