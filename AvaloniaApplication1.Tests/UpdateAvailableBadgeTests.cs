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

        var badge = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UpdateAvailableButton");
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

        var badge = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UpdateAvailableButton");
        Assert.True(badge.IsEffectivelyVisible);

        // The attached Flyout's content isn't in the logical tree until it opens, so it never inherits a DataContext; set it.
        var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(badge));
        var flyoutContent = Assert.IsAssignableFrom<Control>(flyout.Content);
        flyoutContent.DataContext = mainWindowViewModel;
        Dispatcher.UIThread.RunJobs();

        var versionText = flyoutContent.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text is not null && t.Text.Contains("1.2.3"));
        Assert.Equal("Version 1.2.3 is available.", versionText.Text);
    }
}
