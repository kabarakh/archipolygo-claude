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

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void SettingsWindow_DisplaysCurrentAppVersion()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings());
        var window = new SettingsWindow { DataContext = viewModel };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Compare against AppVersionInfo.Current ("dev" locally) rather than hardcoding it.
        var expectedText = $"Version {AppVersionInfo.Current}";
        var versionText = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == expectedText);

        Assert.NotNull(versionText);
    }

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

        // The version is still shown, just not in the hidden "Check for updates" row.
        var expectedVersionText = $"Version {AppVersionInfo.Current}";
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == expectedVersionText && t.IsEffectivelyVisible);
    }
}
