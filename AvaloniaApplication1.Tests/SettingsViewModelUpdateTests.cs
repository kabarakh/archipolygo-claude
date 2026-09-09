using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): the Settings dialog's "Check for
/// updates" button (Feature-Plaene/Archiv/Auto-Update.md) - plain callback-reaction
/// logic, no Avalonia/Velopack involved, same pattern as
/// <see cref="ConnectionEditorViewModelTrackerTests"/>.
/// </summary>
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

    /// <summary>
    /// On an unmanaged install, the command is a guaranteed no-op even if
    /// somehow invoked (the button that normally triggers it is hidden - see
    /// SettingsWindowTests - but this keeps the two in sync regardless of
    /// how it ends up called) - a check there would only ever silently find
    /// nothing anyway, which would misleadingly print "you're up to date."
    /// </summary>
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
