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

public class MainWindowProgressTests
{
    /// <summary>Scoped to the TabControl: Dashboard Overview rows reuse the same bar, so an unscoped search is ambiguous.</summary>
    private static Grid FindCombinedBar(MainWindow window, GroupViewModel group) =>
        window.GetVisualDescendants().OfType<TabControl>().Single()
            .GetVisualDescendants().OfType<Grid>().Single(g => g.DataContext == group && g.ColumnDefinitions.Count == 4);

    /// <summary>Tab content only materializes once the TabControl is visible, and the app starts on the Dashboard.</summary>
    private static void ShowOnTabsView(MainWindow window, MainWindowViewModel viewModel)
    {
        window.Show();
        viewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void CombinedBar_HiddenUntilAnOwnSlotHasSynced_ThenShowsJustOwnSegments_NoTrackerConfigured()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        var combinedBar = FindCombinedBar(window, group);
        Assert.False(combinedBar.IsEffectivelyVisible, "no configured slot has synced yet - the bar must stay hidden rather than show a misleading 0/0.");

        // No tracker configured: the "other" segments are zero width.
        group.Group.Slots[0].LocationsChecked = 7;
        group.Group.Slots[0].LocationsTotal = 20;
        Dispatcher.UIThread.RunJobs();

        Assert.True(combinedBar.IsEffectivelyVisible);
        var widths = combinedBar.ColumnDefinitions.Select(c => c.Width.Value).ToList();
        Assert.Equal(new double[] { 7, 13, 0, 0 }, widths); // own-done, own-open, other-done, other-open
    }

    [AvaloniaFact]
    public void Tier2Section_HiddenWithoutATrackerId_ShownOnceOneIsConfigured()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        var refreshButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Refresh") && b.DataContext == group);

        // IsEffectivelyVisible, since only an ancestor's IsVisible is bound.
        Assert.False(refreshButton.IsEffectivelyVisible, "Tier 2 section must stay hidden entirely for a group with no tracker id configured.");

        group.Group.TrackerId = "some-tracker-id";
        Dispatcher.UIThread.RunJobs();

        Assert.True(refreshButton.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void RefreshButtonClick_ReachesTheConfiguredTrackerService_WithThisGroupsTrackerId()
    {
        var connectionManager = new FakeConnectionManager();
        var trackerService = new FakeMultiworldTrackerService();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, trackerService);
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.TrackerId = "tracker-xyz";

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        // Setting TrackerId already triggered one auto-refresh; clear it so only the click is asserted.
        trackerService.GetProgressCalls.Clear();

        var refreshButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Refresh") && b.DataContext == group);

        var localCenter = new Avalonia.Point(refreshButton.Bounds.Width / 2, refreshButton.Bounds.Height / 2);
        var pointInWindow = refreshButton.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // The fake records synchronously before any await, so this doesn't race the fire-and-forget command.
        Assert.Contains("tracker-xyz", trackerService.GetProgressCalls);
    }

    [AvaloniaFact]
    public void CombinedBar_HiddenUntilThereIsAnyDataAtAll()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.TrackerId = "tracker-xyz"; // tracker configured, but no data fetched/synced yet

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        Assert.False(FindCombinedBar(window, group).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void CombinedBar_OneBarForTheWholeRoom_NotOnePerPlayer_OwnSegmentsFirstThenOthers()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        group.Group.Slots[0].LocationsChecked = 30;
        group.Group.Slots[0].LocationsTotal = 100;

        // "Own" comes from Tier 1, not from matching tracker players to configured slots.
        group.Group.TrackerId = "tracker-xyz";
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 1, ChecksDone = 30, ChecksTotal = 100 });
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 2, ChecksDone = 50, ChecksTotal = 200 });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        // FindCombinedBar uses Single(): one bar, not one per tracked player.
        var combinedBar = FindCombinedBar(window, group);
        Assert.True(combinedBar.IsEffectivelyVisible);

        // Own segments first, then others': own done=30, own open=70, other done=50, other open=150
        var widths = combinedBar.ColumnDefinitions.Select(c => c.Width.Value).ToList();
        Assert.Equal(new double[] { 30, 70, 50, 150 }, widths);
    }

    [AvaloniaFact]
    public void CombinedBar_Tooltip_ShowsLegendMatchingTheViewModel()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.Slots[0].LocationsChecked = 30;
        group.Group.Slots[0].LocationsTotal = 100;
        group.Group.TrackerId = "tracker-xyz";
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 1, ChecksDone = 30, ChecksTotal = 100 });
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 2, ChecksDone = 50, ChecksTotal = 200 });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        var combinedBar = FindCombinedBar(window, group);

        // Tooltip content isn't attached to the tree until the popup opens, so it never inherits a DataContext; set it manually.
        var tooltipContent = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(combinedBar));
        tooltipContent.DataContext = group;
        Dispatcher.UIThread.RunJobs();

        var legendTexts = tooltipContent.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).Where(t => t is not null).ToList();

        Assert.Contains(group.OwnChecksDoneLegendText, legendTexts);
        Assert.Contains(group.OwnChecksOpenLegendText, legendTexts);
        Assert.Contains(group.OtherChecksDoneLegendText, legendTexts);
        Assert.Contains(group.OtherChecksOpenLegendText, legendTexts);
    }

    [AvaloniaFact]
    public void CombinedBar_Tooltip_NoTrackerConfigured_HidesOthersRows_ShowsHint()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.Slots[0].LocationsChecked = 10;
        group.Group.Slots[0].LocationsTotal = 20;

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        ShowOnTabsView(window, mainWindowViewModel);

        var combinedBar = FindCombinedBar(window, group);
        var tooltipContent = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(combinedBar));
        tooltipContent.DataContext = group;
        Dispatcher.UIThread.RunJobs();

        var otherRows = tooltipContent.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == group.OtherChecksDoneLegendText || t.Text == group.OtherChecksOpenLegendText)
            .ToList();
        Assert.All(otherRows, t => Assert.False(t.IsEffectivelyVisible));

        var hint = tooltipContent.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text is not null && t.Text.Contains("Add a multiworld tracker"));
        Assert.True(hint.IsEffectivelyVisible);
    }
}
