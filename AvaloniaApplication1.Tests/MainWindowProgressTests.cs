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
/// Kategorie C (Test-Umsetzungsplan.md): Feature-Plaene/Archiv/Fortschrittsanzeigen.md's
/// two progress sections in <see cref="MainWindow"/>'s tab content, against
/// the real <c>.axaml</c> and a real layout pass - not just the ViewModel
/// arithmetic already covered by <see cref="GroupViewModelRoomProgressTests"/>.
/// </summary>
public class MainWindowProgressTests
{
    /// <summary>Finds the one combined progress bar Grid (own+other done/open, four Star-weighted columns) for <paramref name="group"/>.</summary>
    private static Grid FindCombinedBar(MainWindow window, GroupViewModel group) =>
        window.GetVisualDescendants().OfType<Grid>().Single(g => g.DataContext == group && g.ColumnDefinitions.Count == 4);

    [AvaloniaFact]
    public void CombinedBar_HiddenUntilAnOwnSlotHasSynced_ThenShowsJustOwnSegments_NoTrackerConfigured()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combinedBar = FindCombinedBar(window, group);
        Assert.False(combinedBar.IsEffectivelyVisible, "no configured slot has synced yet - the bar must stay hidden rather than show a misleading 0/0.");

        // Fallback case: no tracker configured at all - the bar still shows,
        // just with the two "other" segments at zero width (see
        // GroupViewModel.HasAnyProgress's doc comment).
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
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var refreshButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Refresh") && b.DataContext == group);

        // IsEffectivelyVisible (not the button's own IsVisible, which is
        // never itself bound - only its ancestor StackPanel's is) accounts
        // for the whole visual-ancestor chain being hidden.
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
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Setting TrackerId above already triggers one lazy auto-refresh (see
        // GroupViewModel.OnGroupPropertyChanged) since the tab is selected by
        // default - clear that call so this test only asserts on the button
        // click itself.
        trackerService.GetProgressCalls.Clear();

        var refreshButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Refresh") && b.DataContext == group);

        var localCenter = new Avalonia.Point(refreshButton.Bounds.Width / 2, refreshButton.Bounds.Height / 2);
        var pointInWindow = refreshButton.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // FakeMultiworldTrackerService records the call synchronously, at the
        // very start of GetProgressAsync, before any awaited continuation -
        // so this assertion doesn't race the (fire-and-forget) async command,
        // unlike asserting on its eventual result would.
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
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(FindCombinedBar(window, group).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void CombinedBar_OneBarForTheWholeRoom_NotOnePerPlayer_OwnSegmentsFirstThenOthers()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        // Tier 1: this app's own configured slot has synced 30/100 via its
        // own live connection.
        group.Group.Slots[0].LocationsChecked = 30;
        group.Group.Slots[0].LocationsTotal = 100;

        // Tier 2: the tracker reports the whole room, including a player that
        // isn't this app's own slot at all (see OwnChecksDone's doc comment
        // for why "own" comes from Tier 1 rather than trying to match this
        // set against configured slots).
        group.Group.TrackerId = "tracker-xyz";
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 1, ChecksDone = 30, ChecksTotal = 100 });
        group.MultiworldProgress.Add(new PlayerProgress { Team = 0, Player = 2, ChecksDone = 50, ChecksTotal = 200 });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // FindCombinedBar itself asserts there's exactly one such Grid (Single) -
        // not one element per tracked player, even though there are two
        // players in MultiworldProgress above.
        var combinedBar = FindCombinedBar(window, group);
        Assert.True(combinedBar.IsEffectivelyVisible);

        // Own segments first (done, open), then others' - not grouped by
        // done/open - see the Grid's own doc comment in MainWindow.axaml.
        // own done=30, own open=100-30=70, other done=80-30=50, other open=300-100-50=150
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
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var combinedBar = FindCombinedBar(window, group);

        // ToolTip.Tip's content is a realized control tree the moment the
        // Grid itself loads (it's inline XAML content, not a lazily-templated
        // popup), but it isn't attached to the visual/logical tree until the
        // tooltip popup actually opens, so its bindings never inherited a
        // DataContext on their own - force the same one the Grid itself has,
        // which is enough for its (already-realized) bindings to resolve
        // without needing to simulate a real hover-and-wait.
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
        // No TrackerId set at all - the fallback case.

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

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
