using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>Kategorie C (Test-Umsetzungsplan.md): SettingsView_DisplaysCurrentAppVersion.</summary>
public class SettingsWindowTests
{
    [AvaloniaFact]
    public void SettingsWindow_DisplaysCurrentAppVersion()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings());
        var window = new SettingsWindow { DataContext = viewModel };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // AppVersionInfo.Current is "dev" for every local/test build (see its
        // doc comment) and only ever a real tag for a release.yml publish -
        // compare against it directly rather than hardcoding "dev", so this
        // doesn't need updating if that convention ever changes.
        var expectedText = $"Version {AppVersionInfo.Current}";
        var versionText = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == expectedText);

        Assert.NotNull(versionText);
    }

    /// <summary>
    /// Feature-Plaene/Archiv/Auto-Update.md: the default (managed-install-or-macOS)
    /// case - "Check for updates" is the normal, clickable path, and the
    /// unmanaged-install hint is nowhere to be seen.
    /// </summary>
    [AvaloniaFact]
    public void SettingsWindow_ManagedInstall_ShowsCheckForUpdatesButton_NoHint()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings(), showUnmanagedInstallHint: false);
        var window = new SettingsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var checkButton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => Equals(b.Content, "Check for updates"));
        Assert.NotNull(checkButton);
        Assert.True(checkButton!.IsEffectivelyVisible);

        var hint = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text is not null && t.Text.Contains("manually downloaded build"));
        Assert.False(hint?.IsEffectivelyVisible ?? false);
    }

    /// <summary>The unmanaged-install case - the hint replaces "Check for updates" entirely, not just alongside it.</summary>
    [AvaloniaFact]
    public void SettingsWindow_UnmanagedInstall_ShowsHint_HidesCheckForUpdatesButton()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings(), showUnmanagedInstallHint: true);
        var window = new SettingsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var checkButton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => Equals(b.Content, "Check for updates"));
        Assert.False(checkButton?.IsEffectivelyVisible ?? false);

        var hint = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text is not null && t.Text.Contains("manually downloaded build"));
        Assert.NotNull(hint);
        Assert.True(hint!.IsEffectivelyVisible);

        // The version number is still shown somewhere - just not in the
        // now-hidden "Check for updates" row.
        var expectedVersionText = $"Version {AppVersionInfo.Current}";
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == expectedVersionText && t.IsEffectivelyVisible);
    }
}
