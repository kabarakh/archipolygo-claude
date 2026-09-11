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

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): Feature-Plaene/Dashboard-Tab.md's
/// toggle button, view swap and row-click navigation, against the real
/// <c>.axaml</c> and a real layout pass - not just <see cref="DashboardViewModel"/>'s
/// own arithmetic/filtering, already covered by <see cref="DashboardViewModelTests"/>.
/// </summary>
public class DashboardTabTests
{
    private static MainWindow ShowWindow(MainWindowViewModel viewModel)
    {
        // Wider than MainWindowProgressTests' 900 - the toolbar now has one
        // more button ("Dashboard"/"Tab View") than when that width was
        // chosen, and a horizontal StackPanel doesn't wrap; a too-narrow
        // window leaves it laid out past the window's own right edge, where
        // a simulated click never lands.
        var window = new MainWindow { DataContext = viewModel, Width = 1200, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void ClickButton(MainWindow window, Button button)
    {
        var localCenter = new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        var pointInWindow = button.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The app opens on the Dashboard by default (dev request, 2026-09-11) - see <see cref="MainWindowViewModel.IsDashboardVisible"/>'s doc comment.</summary>
    [AvaloniaFact]
    public void Dashboard_IsShownByDefaultOnStartup()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);

        Assert.True(mainWindowViewModel.IsDashboardVisible);
        Assert.False(window.GetVisualDescendants().OfType<TabControl>().Single().IsEffectivelyVisible);
        Assert.True(window.GetVisualDescendants().OfType<DashboardView>().Single().IsEffectivelyVisible);

        // The toggle button already reads "Tab View" on first render, not "Dashboard".
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

        // Starts on the Dashboard by default (see Dashboard_IsShownByDefaultOnStartup) -
        // click "Tab View" first to reach the TabControl side.
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
        var overviewList = dashboardView.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "OverviewListBox");

        overviewList.SelectedItem = group2;
        Dispatcher.UIThread.RunJobs();

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
        group1.UnreadEventCount = 3;
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

    /// <summary>
    /// "Add slot..."/"Edit server..."/"Remove server" all act on
    /// <see cref="MainWindowViewModel.SelectedGroup"/> - hidden while the
    /// Dashboard is shown instead of the tabs, since there's no single
    /// "current" server to visibly apply them to there. "Add server..." and
    /// "Disconnect all" don't depend on a selected group, so they stay.
    /// </summary>
    [AvaloniaFact]
    public void GroupScopedToolbarButtons_HiddenWhileDashboardVisible_ShownOtherwise()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = ShowWindow(mainWindowViewModel);

        // Starts on the Dashboard by default - switch to the tabs side first
        // so the "shown otherwise" half of this test starts from a clean baseline.
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
        // Neither depends on SelectedGroup, so both stay visible regardless.
        Assert.True(addServer.IsEffectivelyVisible);
        Assert.True(disconnectAll.IsEffectivelyVisible);

        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        Assert.True(addSlot.IsEffectivelyVisible);
        Assert.True(editServer.IsEffectivelyVisible);
        Assert.True(removeServer.IsEffectivelyVisible);
    }

    /// <summary>The Dashboard toggle is the first button in the toolbar, immediately followed by a divider separating it from the group-scoped actions - see Feature-Plaene/Archiv/Dashboard-Tab.md's Status note.</summary>
    [AvaloniaFact]
    public void DashboardToggleButton_IsFirstInToolbar()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        var window = ShowWindow(mainWindowViewModel);

        var toolbar = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Orientation == Avalonia.Layout.Orientation.Horizontal);
        var children = toolbar.Children;

        // Starts on the Dashboard by default, so the toggle button's own
        // label already reads "Tab View" on first render (see
        // Dashboard_IsShownByDefaultOnStartup) - position, not label, is
        // what this test is actually pinning down.
        var toggleButton = Assert.IsType<Button>(children.OfType<Button>().First());
        Assert.Equal("Tab View", toggleButton.Content);

        var toggleIndex = children.IndexOf(toggleButton);
        Assert.True(children[toggleIndex + 1] is Border, "expected a divider Border directly after the Dashboard toggle button.");
    }

    /// <summary>Finds a specific per-row icon button by its <c>Tag</c> (see DashboardView.axaml's comment on why Tag, not Name, is used here) for the given group's Overview row.</summary>
    private static Button FindRowIcon(DashboardView dashboardView, GroupViewModel group, string tag) =>
        dashboardView.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("icon-button") && b.DataContext == group && (string?)b.Tag == tag);

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

    /// <summary>
    /// Connect/Disconnect icons bind straight to <see cref="GroupViewModel.ConnectCommand"/>/
    /// <see cref="GroupViewModel.DisconnectCommand"/> - AddNewGroup already
    /// connects its one slot as leader, so this starts connected (Disconnect
    /// shown) and exercises both directions.
    /// </summary>
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

    /// <summary>"Add slot" is grayed out (not hidden) while disconnected - see the Status note's "user decision" on this.</summary>
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

    /// <summary>
    /// Add slot/Edit server open a dialog this test can't safely drive
    /// headlessly (no existing test in this suite does - see
    /// <see cref="ConnectionEditorWindowProgressTests"/>, which shows the
    /// window directly rather than through a real modal ShowDialogAsync) -
    /// instead this locks in the handoff <see cref="MainWindow"/> relies on:
    /// clicking the icon raises <see cref="DashboardView.AddSlotRequested"/>/
    /// <see cref="DashboardView.EditServerRequested"/> with exactly the
    /// clicked row's own group.
    /// </summary>
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

    /// <summary>Remove needs no dialog, so this exercises the full path: icon click → <see cref="DashboardView.RemoveServerRequested"/> → <see cref="MainWindow"/>'s subscription → <see cref="MainWindowViewModel.RemoveGroupAsync"/>.</summary>
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
        // Removing a row that wasn't the selected tab must not change SelectedGroup.
        Assert.Equal(group1, mainWindowViewModel.SelectedGroup);
        // Removing from the Dashboard must not itself navigate away from it.
        Assert.True(mainWindowViewModel.IsDashboardVisible);
    }

    /// <summary>
    /// A row's whole surface otherwise navigates to that server's tab on
    /// click (see <see cref="OverviewRowClick_SelectsThatGroup_AndLeavesDashboard"/>) -
    /// clicking one of its icon buttons must not also trigger that, or every
    /// icon click would have the confusing side effect of also leaving the
    /// Dashboard. Verified rather than assumed, per Button's own
    /// PointerPressed handling marking the event handled before the row's
    /// SelectionChanged can fire.
    /// </summary>
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

    // ── Shared Events view (dev follow-up, 2026-09-11) ──

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

    /// <summary>
    /// The Events panel's Slot dropdown is not a display filter (unlike the
    /// Hints one) - picking a slot there is the exact same real leader
    /// switch as the tab's own "Chat as" dropdown, since it binds to the
    /// same <see cref="GroupViewModel.SelectedChatSlot"/>/<see cref="GroupViewModel.Slots"/>.
    /// </summary>
    /// <summary>
    /// The "Send as" Slot dropdown (down by the message box, deliberately
    /// separate from the filter row up top - dev decision 2026-09-12) is the
    /// real leader switch, same as the tab's own "Chat as" dropdown.
    /// </summary>
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

    /// <summary>
    /// The Events filter row's Slot dropdown (top) is a pure display filter,
    /// same as the Hints one - selecting it must never touch the leader.
    /// </summary>
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
        // SetUpSingleGroupDashboard's group is already connected (AddNewGroup
        // connects its one slot as leader), so Send becomes usable too.
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

        // GroupViewModel.SendMessageAsync clears MessageToSend once it runs -
        // the only reliable, synchronously-observable signal (this session's
        // FakeConnectionManager.SendMessageAsync itself records nothing) that
        // the click actually reached this specific group's own SendMessageCommand.
        Assert.Equal(string.Empty, group.MessageToSend);
    }

    /// <summary>Filtering (SelectedEventsServerFilter) and "who's chatting" (SelectedSendServerGroup) are independent - picking one must never change the other.</summary>
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
}
