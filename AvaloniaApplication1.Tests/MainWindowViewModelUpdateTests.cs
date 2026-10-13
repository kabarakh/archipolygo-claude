using System.Threading.Tasks;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>Only the view model's reaction to an update result is testable; the Velopack download/restart can't run headless.</summary>
public class MainWindowViewModelUpdateTests
{
    private static MainWindowViewModel MakeViewModel(FakeUpdateService? updateService = null) =>
        new(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService(), updateService);

    [Fact]
    public void StartupCheck_UpdateAvailable_SetsIsUpdateAvailableAndVersion()
    {
        var updateService = new FakeUpdateService { NextCheckResult = "1.2.3" };

        var mainWindowViewModel = MakeViewModel(updateService);

        // FakeUpdateService completes synchronously, so the constructor's fire-and-forget check has already run.
        Assert.True(mainWindowViewModel.IsUpdateAvailable);
        Assert.Equal("1.2.3", mainWindowViewModel.NewUpdateVersion);
        Assert.Equal(1, updateService.CheckForUpdatesCallCount);
    }

    [Fact]
    public void StartupCheck_NoUpdateAvailable_LeavesIsUpdateAvailableFalse()
    {
        var updateService = new FakeUpdateService(); // NextCheckResult stays null

        var mainWindowViewModel = MakeViewModel(updateService);

        Assert.False(mainWindowViewModel.IsUpdateAvailable);
        Assert.Null(mainWindowViewModel.NewUpdateVersion);
        Assert.Equal(1, updateService.CheckForUpdatesCallCount);
    }

    [Fact]
    public void NoUpdateServiceSupplied_NeverCrashes_StaysUnavailable()
    {
        var mainWindowViewModel = MakeViewModel(updateService: null);

        Assert.False(mainWindowViewModel.IsUpdateAvailable);
        Assert.Null(mainWindowViewModel.NewUpdateVersion);
    }

    [Fact]
    public async Task UpdateNowCommand_ForwardsToDownloadAndApplyUpdateAsync()
    {
        var updateService = new FakeUpdateService { NextCheckResult = "1.2.3" };
        var mainWindowViewModel = MakeViewModel(updateService);

        await mainWindowViewModel.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(1, updateService.DownloadAndApplyUpdateCallCount);
    }

    [Fact]
    public async Task UpdateNowCommand_NoUpdateServiceSupplied_DoesNothingRatherThanThrowing()
    {
        var mainWindowViewModel = MakeViewModel(updateService: null);

        await mainWindowViewModel.UpdateNowCommand.ExecuteAsync(null);
        // No exception - that's the assertion.
    }

    [Fact]
    public async Task ManualCheckForUpdatesAsync_UpdatesBadgeState_SameAsStartupCheck()
    {
        // The Settings dialog's "Check for updates" button calls this same method, so a manual check also updates the badge.
        var updateService = new FakeUpdateService();
        var mainWindowViewModel = MakeViewModel(updateService);
        Assert.False(mainWindowViewModel.IsUpdateAvailable);

        updateService.NextCheckResult = "2.0.0";
        var result = await mainWindowViewModel.CheckForUpdatesAsync();

        Assert.Equal("2.0.0", result);
        Assert.True(mainWindowViewModel.IsUpdateAvailable);
        Assert.Equal("2.0.0", mainWindowViewModel.NewUpdateVersion);
    }

    [Fact]
    public void ShowUnmanagedInstallHint_ManagedInstall_IsFalse()
    {
        var updateService = new FakeUpdateService { IsManagedInstall = true, SupportsManagedInstall = true };
        var mainWindowViewModel = MakeViewModel(updateService);

        Assert.False(mainWindowViewModel.ShowUnmanagedInstallHint);
    }

    [Fact]
    public void ShowUnmanagedInstallHint_UnmanagedInstall_OnASupportedPlatform_IsTrue()
    {
        var updateService = new FakeUpdateService { IsManagedInstall = false, SupportsManagedInstall = true };
        var mainWindowViewModel = MakeViewModel(updateService);

        Assert.True(mainWindowViewModel.ShowUnmanagedInstallHint);
    }

    /// <summary>No Velopack package exists for macOS, so the hint would point at nothing.</summary>
    [Fact]
    public void ShowUnmanagedInstallHint_PlatformDoesNotSupportManagedInstallAtAll_IsFalse()
    {
        var updateService = new FakeUpdateService { IsManagedInstall = false, SupportsManagedInstall = false };
        var mainWindowViewModel = MakeViewModel(updateService);

        Assert.False(mainWindowViewModel.ShowUnmanagedInstallHint);
    }

    [Fact]
    public void ShowUnmanagedInstallHint_NoUpdateServiceSupplied_IsFalse()
    {
        var mainWindowViewModel = MakeViewModel(updateService: null);

        Assert.False(mainWindowViewModel.ShowUnmanagedInstallHint);
    }
}
