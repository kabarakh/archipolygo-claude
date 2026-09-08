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
}
