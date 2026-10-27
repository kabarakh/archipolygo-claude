using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Converters;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>"Hide goaled" filters, the 🏁 marker and the goal-state reads behind them.</summary>
public class GoaledSlotTests
{
    private static GroupViewModel MakeTabGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static EventEntry ItemLine(string text, RoomSlotStatus? receiver) =>
        new() { Text = text, Type = EventType.ItemReceived, Receiver = receiver };

    private static HintEntry Hint(string key, RoomSlotStatus receiving, RoomSlotStatus finding) =>
        new() { Key = key, ItemName = key, ReceivingSlot = receiving, FindingSlot = finding };

    // ── Register ──

    [Fact]
    public void GetRoomSlotStatus_ReturnsTheSameInstancePerSlotNumber()
    {
        var group = MakeTabGroup();

        Assert.Same(group.GetRoomSlotStatus(3), group.GetRoomSlotStatus(3));
        Assert.NotSame(group.GetRoomSlotStatus(3), group.GetRoomSlotStatus(4));
    }

    [Fact]
    public void ApplyGoals_SameSeed_NeverClearsAGoal()
    {
        var group = MakeTabGroup();
        group.ApplyGoals("seed", new[] { 2 });

        group.ApplyGoals("seed", Array.Empty<int>());
        group.ApplyGoals(seed: null, Array.Empty<int>());

        Assert.True(group.GetRoomSlotStatus(2).HasGoal);
    }

    [Fact]
    public void ApplyGoals_NewSeed_ClearsGoalsOfTheOldRoom()
    {
        var group = MakeTabGroup();
        group.ApplyGoals("old-seed", new[] { 2 });

        group.ApplyGoals("new-seed", new[] { 5 });

        Assert.False(group.GetRoomSlotStatus(2).HasGoal);
        Assert.True(group.GetRoomSlotStatus(5).HasGoal);
    }

    [Fact]
    public void ApplyGoals_RaisesGoalStatesChanged_OnlyOnAChange()
    {
        var group = MakeTabGroup();
        var raised = 0;
        group.GoalStatesChanged += () => raised++;

        group.ApplyGoals("seed", new[] { 2 });
        group.ApplyGoals("seed", new[] { 2 });

        Assert.Equal(1, raised);
    }

    // ── Tab filters ──

    [Fact]
    public void HideGoaledEvents_HidesItemsToAGoaledReceiver_KeepsOthersAndLinesWithoutReceiver()
    {
        var group = MakeTabGroup();
        group.Events.Add(ItemLine("to-goaled", group.GetRoomSlotStatus(2)));
        group.Events.Add(ItemLine("release-to-other", group.GetRoomSlotStatus(3)));
        group.Events.Add(new EventEntry { Text = "chat", Type = EventType.Chat });
        group.ApplyGoals("seed", new[] { 2 });

        Assert.Equal(3, group.VisibleEvents.Count());

        group.HideGoaledEvents = true;

        Assert.Equal(new[] { "release-to-other", "chat" }, group.VisibleEvents.Select(e => e.Text));
    }

    [Fact]
    public void HideGoaledEvents_ALaterGoal_HidesAnOlderEntry_AndRaisesVisibleEvents()
    {
        var group = MakeTabGroup();
        group.Events.Add(ItemLine("older", group.GetRoomSlotStatus(2)));
        group.HideGoaledEvents = true;
        var raised = new List<string?>();
        group.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        group.ApplyGoals("seed", new[] { 2 });

        Assert.Contains(nameof(GroupViewModel.VisibleEvents), raised);
        Assert.Empty(group.VisibleEvents);
    }

    [Fact]
    public void HideGoaledHints_HidesHintsForAGoaledReceiver_EvenWhenAnOwnSlotFindsIt()
    {
        var group = MakeTabGroup();
        group.SelectedHintFilter = HintFilter.All;
        group.Hints.Add(Hint("for-goaled", receiving: group.GetRoomSlotStatus(2), finding: group.GetRoomSlotStatus(1)));
        group.Hints.Add(Hint("goaled-finder", receiving: group.GetRoomSlotStatus(1), finding: group.GetRoomSlotStatus(2)));
        group.ApplyGoals("seed", new[] { 2 });

        group.HideGoaledHints = true;

        Assert.Equal(new[] { "goaled-finder" }, group.VisibleHints.Select(h => h.Key));
    }

    [Fact]
    public void FilterSummaries_NameTheHideGoaledSwitch()
    {
        var group = MakeTabGroup();
        group.HideGoaledEvents = true;
        group.HideGoaledHints = true;

        Assert.Contains("No goaled", group.EventFilterSummaryText);
        Assert.Contains("No goaled", group.RightPanelFilterSummaryText);
    }

    // ── Dashboard filters ──

    [Fact]
    public void Dashboard_HideGoaled_HasItsOwnState_AndFollowsLaterGoals()
    {
        var group = MakeTabGroup();
        group.Events.Add(ItemLine("to-two", group.GetRoomSlotStatus(2)));
        group.Hints.Add(Hint("for-two", receiving: group.GetRoomSlotStatus(2), finding: group.GetRoomSlotStatus(1)));
        var dashboard = new DashboardViewModel(new ObservableCollection<GroupViewModel> { group }, _ => { }, (_, _, _) => { });
        dashboard.HideGoaledEvents = true;
        dashboard.HideGoaledHints = true;
        var raised = new List<string?>();
        dashboard.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        group.ApplyGoals("seed", new[] { 2 });

        Assert.Contains(nameof(DashboardViewModel.VisibleEvents), raised);
        Assert.Contains(nameof(DashboardViewModel.VisibleHints), raised);
        Assert.Empty(dashboard.VisibleEvents);
        Assert.Empty(dashboard.VisibleHints);
        Assert.False(group.HideGoaledEvents);
        Assert.Single(group.VisibleEvents);
        Assert.Contains("No goaled", dashboard.EventFilterSummaryText);
        Assert.Contains("No goaled", dashboard.HintFilterSummaryText);
    }

    // ── Converter ──

    [Theory]
    [InlineData(true, "🏁 Bob")]
    [InlineData(false, "Bob")]
    public void GoalMarkerTextConverter_PrefixesTheMarkerOnlyWithAGoal(bool hasGoal, string expected)
    {
        var text = new GoalMarkerTextConverter().Convert(new List<object?> { "Bob", hasGoal }, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void GoalMarkerTextConverter_UnsetGoal_ShowsThePlainName()
    {
        var text = new GoalMarkerTextConverter().Convert(new List<object?> { "Bob", AvaloniaProperty.UnsetValue }, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Bob", text);
    }

    // ── Translator and ConnectionManager ──

    private static FakeArchipelagoSession MakeRoomSession()
    {
        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[]
        {
            new PlayerInfo(0, 0, "Server", null, "Archipelago", null, null),
            new PlayerInfo(0, 1, "Alice", null, "Game", null, null),
            new PlayerInfo(0, 2, "Bob", null, "Game", null, null),
            new PlayerInfo(0, 3, "Carol", null, "Game", null, null),
            new PlayerInfo(0, 4, "Link", null, "Game", null, new[] { 2, 3 }),
        };
        return session;
    }

    [AvaloniaFact]
    public async Task ReadGoalStatesAsync_MarksGoaledSlots_AndSkipsSlotZeroAndGroups()
    {
        var group = MakeTabGroup();
        var session = MakeRoomSession();
        session.DataStorage.ClientStatuses[2] = ArchipelagoClientState.ClientGoal;
        session.DataStorage.ClientStatuses[3] = ArchipelagoClientState.ClientPlaying;
        var translator = new SessionEventTranslator(new NoOpMessageHistoryService(), new NoOpHintService());

        await translator.ReadGoalStatesAsync(group, session);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { 1, 2, 3 }, session.DataStorage.RequestedClientStatusSlots.OrderBy(s => s));
        Assert.Equal(new[] { 2 }, group.GoaledSlotNumbers);
        Assert.False(group.GetRoomSlotStatus(4).HasGoal);
    }

    [AvaloniaFact]
    public async Task ItemLinkGroup_HasGoal_OnceAllMembersHaveIt()
    {
        var group = MakeTabGroup();
        var session = MakeRoomSession();
        session.DataStorage.ClientStatuses[2] = ArchipelagoClientState.ClientGoal;
        var translator = new SessionEventTranslator(new NoOpMessageHistoryService(), new NoOpHintService());
        await translator.ReadGoalStatesAsync(group, session);
        Dispatcher.UIThread.RunJobs();

        translator.OnGoalReached(group, session, slotNumber: 3);
        Dispatcher.UIThread.RunJobs();

        Assert.True(group.GetRoomSlotStatus(3).HasGoal);
        Assert.True(group.GetRoomSlotStatus(4).HasGoal);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_ReadsGoalStates()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var session = MakeRoomSession();
        session.DataStorage.ClientStatuses[2] = ArchipelagoClientState.ClientGoal;
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, group.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.GetRoomSlotStatus(2).HasGoal);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_FailedGoalRead_KeepsTheLeaderConnected()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var session = MakeRoomSession();
        session.DataStorage.ClientStatusFailure = new InvalidOperationException("no storage");
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, group.Slots[0]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(group.Slots[0].Id, vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
        Assert.Empty(vm.GoaledSlotNumbers);
    }

    // ── Attention ──

    private static HintSnapshot Snapshot(Guid slotId, int receivingPlayer) => new()
    {
        Key = $"hint-{receivingPlayer}",
        SlotId = slotId,
        ReceivingPlayer = receivingPlayer,
        FindingPlayer = 1,
        ReceivingPlayerName = "Bob",
        FindingPlayerName = "Alice",
        ItemName = "Sword",
        LocationName = "Chest",
        ItemFlags = ItemFlags.None,
        ReceivingPlayerKind = EventTextSegmentKind.OtherSlotName,
        FindingPlayerKind = EventTextSegmentKind.OwnSlotName,
    };

    [AvaloniaFact]
    public void LiveHint_ForAGoaledReceiver_ReportsNoAttention_ButStillGetsTheReceiverReference()
    {
        var group = MakeTabGroup();
        group.ApplyGoals("seed", new[] { 2 });
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.SyncHints(group, new[] { Snapshot(group.Group.Slots[0].Id, receivingPlayer: 2) }, isLive: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(attentionTracker.Reports);
        Assert.Same(group.GetRoomSlotStatus(2), group.Hints.Single().ReceivingSlot);
        Assert.Same(group.GetRoomSlotStatus(2), group.Events.Single().Receiver);
    }

    // ── Views ──

    private static (MainWindow window, GroupViewModel group) ShowGroupTab()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 1200, Height = 550 };
        window.Show();
        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
        return (window, group);
    }

    private static IEnumerable<string?> EventTexts(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EventsListBox")
              .GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    [AvaloniaFact]
    public void EventLog_ShowsTheMarker_AlsoOnAnAlreadyRenderedEntry()
    {
        var (window, group) = ShowGroupTab();
        var bob = group.GetRoomSlotStatus(2);
        group.Events.Add(new EventEntry
        {
            Text = "Bob found Sword",
            Type = EventType.Chat,
            Segments = new[]
            {
                new EventTextSegment("Bob", EventTextSegmentKind.OtherSlotName, bob),
                new EventTextSegment(" found Sword", EventTextSegmentKind.PlainText),
            },
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Bob", EventTexts(window));

        group.ApplyGoals("seed", new[] { 2 });
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("🏁 Bob", EventTexts(window));
    }

    [AvaloniaFact]
    public void HideGoaledCheckBoxes_DriveTheTabFilters()
    {
        var (window, group) = ShowGroupTab();
        var checkBoxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();

        checkBoxes.Single(c => c.Name == "HideGoaledEventsCheckBox").IsChecked = true;
        checkBoxes.Single(c => c.Name == "HideGoaledHintsCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(group.HideGoaledEvents);
        Assert.True(group.HideGoaledHints);
    }

    [AvaloniaFact]
    public void Dashboard_HideGoaledCheckBoxes_DriveTheDashboardFilters()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 1200, Height = 550 };
        window.Show();
        mainWindowViewModel.Dashboard.SelectedLeftPanel = DashboardLeftPanel.Events;
        Dispatcher.UIThread.RunJobs();
        var checkBoxes = window.GetVisualDescendants().OfType<CheckBox>().ToList();

        checkBoxes.Single(c => c.Name == "DashboardHideGoaledEventsCheckBox").IsChecked = true;
        checkBoxes.Single(c => c.Name == "DashboardHideGoaledHintsCheckBox").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(mainWindowViewModel.Dashboard.HideGoaledEvents);
        Assert.True(mainWindowViewModel.Dashboard.HideGoaledHints);
        Assert.False(mainWindowViewModel.Groups[0].HideGoaledEvents);
    }
}
