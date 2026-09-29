using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Step 4 of the feature-plan archive's <c>Benachrichtigungen.md</c>: which
/// number each native badge gets, and that the windows keep the badge
/// service in sync. The real platform calls (ITaskbarList3 overlay, NSDockTile
/// badge) aren't exercised headlessly - by design nothing hands the real
/// <see cref="UnreadBadgeService"/> to a window in tests (see
/// <see cref="MainWindow.UnreadBadgeService"/>) - so they need a manual check
/// on each OS.
/// </summary>
public class UnreadBadgeTests
{
    private static MainWindowViewModel WithTwoGroups(out GroupViewModel alpha, out GroupViewModel beta)
    {
        var vm = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        vm.AddNewGroup("Alpha", "hostA", 1, string.Empty, "SlotA", autoConnect: false);
        vm.AddNewGroup("Beta", "hostB", 2, string.Empty, "SlotB", autoConnect: false);
        alpha = vm.Groups[0];
        beta = vm.Groups[1];
        return vm;
    }

    [Theory]
    [InlineData(0, 9, null)]
    [InlineData(-1, 9, null)]
    [InlineData(3, 9, "3")]
    [InlineData(9, 9, "9")]
    [InlineData(10, 9, "9+")]
    [InlineData(150, 99, "99+")]
    public void BadgeText_CapsAtMax_AndHidesZero(int count, int max, string? expected)
    {
        Assert.Equal(expected, UnreadBadgeService.BadgeText(count, max));
    }

    [Fact]
    public void MainWindowBadge_CountsDockedGroups_AppBadge_CountsEveryGroup()
    {
        var vm = WithTwoGroups(out var alpha, out var beta);
        vm.DetachGroup(beta);

        alpha.UnreadAttentionCount = 2;
        beta.UnreadAttentionCount = 5;

        Assert.Equal(2, vm.MainWindowBadgeCount);
        Assert.Equal(7, vm.AppBadgeCount);
        Assert.Equal(5, beta.DetachedWindowBadgeCount);
    }

    /// <summary>A detached group's count must still reach the app-wide (Dock) badge - so the main view model watches every group, not only docked ones.</summary>
    [Fact]
    public void DetachedGroupCountChange_RaisesAppBadgeCount()
    {
        var vm = WithTwoGroups(out _, out var beta);
        vm.DetachGroup(beta);
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(MainWindowViewModel.AppBadgeCount);

        beta.UnreadAttentionCount = 1;

        Assert.True(raised);
    }

    [AvaloniaFact]
    public void ShowUnreadBadgeOff_ZeroesEveryBadge_ButKeepsTheTitleCount()
    {
        var vm = WithTwoGroups(out var alpha, out var beta);
        alpha.UnreadAttentionCount = 2;
        beta.UnreadAttentionCount = 1;

        vm.SaveSettings(new AppSettings { ShowUnreadBadge = false });

        Assert.Equal(0, vm.MainWindowBadgeCount);
        Assert.Equal(0, vm.AppBadgeCount);
        Assert.Equal(0, alpha.DetachedWindowBadgeCount);
        Assert.Equal("(3) Archipolygo", vm.MainWindowTitle);
    }

    [AvaloniaFact]
    public void MainWindow_ForwardsCountChanges_ToBadgeService()
    {
        var vm = WithTwoGroups(out var alpha, out _);
        var badges = new FakeUnreadBadgeService();
        var window = new MainWindow { UnreadBadgeService = badges, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        alpha.UnreadAttentionCount = 4;

        Assert.Equal(4, badges.WindowBadges[window]);
        Assert.Equal(4, badges.AppBadge);

        alpha.UnreadAttentionCount = 0;

        Assert.Equal(0, badges.WindowBadges[window]);
        Assert.Equal(0, badges.AppBadge);
    }

    [AvaloniaFact]
    public void DetachedWindow_ForwardsItsGroupsCount_ToItsOwnBadge()
    {
        WithTwoGroups(out var alpha, out _);
        var badges = new FakeUnreadBadgeService();
        var window = DetachedGroupWindow.Create(alpha, new GroupWindowLocator(), _ => { }, badges);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, badges.WindowBadges[window]);

        alpha.UnreadAttentionCount = 3;
        Assert.Equal(3, badges.WindowBadges[window]);

        // No longer tracked once closed.
        window.Close();
        alpha.UnreadAttentionCount = 8;
        Assert.Equal(3, badges.WindowBadges[window]);
    }

    [Fact]
    public void SettingsViewModel_RoundTripsShowUnreadBadge()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings { ShowUnreadBadge = false });

        Assert.False(viewModel.ShowUnreadBadge);
        viewModel.ShowUnreadBadge = true;
        Assert.True(viewModel.TryBuildSettings(out var built));
        Assert.True(built.ShowUnreadBadge);
    }
}
