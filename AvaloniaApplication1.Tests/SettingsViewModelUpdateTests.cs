using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

public class SettingsViewModelUpdateTests
{
    [Fact]
    public async Task CheckForUpdatesCommand_UpdateAvailable_ShowsVersionInStatusText()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings(), checkForUpdatesAsync: () => Task.FromResult<string?>("1.2.3"));

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("Update available: version 1.2.3", viewModel.UpdateCheckStatusText);
        Assert.False(viewModel.IsCheckingForUpdates);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_NoUpdateAvailable_ShowsUpToDate()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings(), checkForUpdatesAsync: () => Task.FromResult<string?>(null));

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal("You're up to date.", viewModel.UpdateCheckStatusText);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_NoCallbackWiredUp_DoesNothingRatherThanThrowing()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings());

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Null(viewModel.UpdateCheckStatusText);
    }

    /// <summary>The button is hidden there anyway; a check would only find nothing and misleadingly report "up to date".</summary>
    [Fact]
    public async Task CheckForUpdatesCommand_UnmanagedInstall_NeverRunsEvenWithACallbackWiredUp()
    {
        var callbackInvoked = false;
        var viewModel = SettingsViewModel.FromSettings(
            new AppSettings(),
            checkForUpdatesAsync: () =>
            {
                callbackInvoked = true;
                return Task.FromResult<string?>("1.2.3");
            },
            showUnmanagedInstallHint: true);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.False(callbackInvoked);
        Assert.Null(viewModel.UpdateCheckStatusText);
    }
}
