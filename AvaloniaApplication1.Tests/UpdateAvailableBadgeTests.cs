using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): Feature-Plaene/Archiv/Auto-Update.md's
/// small dot next to "Settings..." in <see cref="MainWindow"/>, against the
/// real <c>.axaml</c> and a real layout pass.
/// </summary>
public class UpdateAvailableBadgeTests
{
    [AvaloniaFact]
    public void Badge_HiddenWithNoUpdate_ShownOnceOneIsAvailable()
    {
        var updateService = new FakeUpdateService(); // no update yet
        var mainWindowViewModel = new MainWindowViewModel(
            new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService(), updateService);

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var badge = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "UpdateAvailableBadge");
        Assert.False(badge.IsEffectivelyVisible);

        mainWindowViewModel.IsUpdateAvailable = true;
        mainWindowViewModel.NewUpdateVersion = "1.2.3";
        Dispatcher.UIThread.RunJobs();

        Assert.True(badge.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Badge_FlyoutContent_ShowsTheNewVersion()
    {
        var updateService = new FakeUpdateService { NextCheckResult = "1.2.3" };
        var mainWindowViewModel = new MainWindowViewModel(
            new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService(), updateService);

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var badge = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "UpdateAvailableBadge");
        Assert.True(badge.IsEffectivelyVisible);

        // Same "force the DataContext" trick as the combined progress bar's
        // tooltip test - the attached Flyout's content is a realized control
        // tree the moment the Border itself loads, but isn't attached to the
        // logical tree (so its bindings never inherit a DataContext) until
        // the popup actually opens.
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(badge));
        var flyoutContent = Assert.IsAssignableFrom<Control>(flyout.Content);
        flyoutContent.DataContext = mainWindowViewModel;
        Dispatcher.UIThread.RunJobs();

        var versionText = flyoutContent.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text is not null && t.Text.Contains("1.2.3"));
        Assert.Equal("Version 1.2.3 is available.", versionText.Text);
    }
}
