using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kompakteres-Layout.md, Teil A: the compact/normal density
/// setting - <see cref="DensityService"/>, its effect on a real Events row,
/// and the Settings dialog round-trip.
/// </summary>
public class DensityTests
{
    private static FluentTheme Fluent() => Application.Current!.Styles.OfType<FluentTheme>().Single();

    [AvaloniaTheory]
    [InlineData("Compact", DensityStyle.Compact)]
    [InlineData("Normal", DensityStyle.Normal)]
    [InlineData("garbage", DensityStyle.Compact)]
    public void Apply_SetsFluentThemeDensityStyle(string setting, DensityStyle expected)
    {
        DensityService.Apply(setting);

        Assert.Equal(expected, Fluent().DensityStyle);
    }

    [Fact]
    public void AppSettings_DefaultsToCompact()
    {
        Assert.Equal(DensityService.Compact, new AppSettings().UiDensity);
    }

    /// <summary>
    /// Measures the real rendered height of a one-line row in a server tab's
    /// own Events list (not a throwaway ListBox) under both densities - so a
    /// local Padding/MinHeight on that list's items (which would silently
    /// beat the theme resource, see CLAUDE.md's "local value beats a Style
    /// Setter" gotcha) would fail this rather than go unnoticed.
    /// </summary>
    [AvaloniaFact]
    public void CompactDensity_MakesEventRowsShorter()
    {
        var normal = MeasureEventRowHeight(DensityService.Normal);
        var compact = MeasureEventRowHeight(DensityService.Compact);

        Assert.True(compact < normal - 10, $"expected compact rows clearly shorter than normal; normal={normal}, compact={compact}");
    }

    /// <summary>
    /// Compact's row padding is this app's own override (see
    /// <see cref="DensityService"/>), not Fluent's tighter 4,2 - and
    /// switching back to Normal drops the override again instead of leaving
    /// it stuck on.
    /// </summary>
    [AvaloniaFact]
    public void CompactDensity_UsesAppRowPadding_NormalFallsBackToFluent()
    {
        DensityService.Apply(DensityService.Compact);
        Assert.Equal(new Thickness(4, 4), MeasureEventRowPadding());

        DensityService.Apply(DensityService.Normal);
        Assert.NotEqual(new Thickness(4, 4), MeasureEventRowPadding());

        DensityService.Apply(DensityService.Compact);
    }

    private static Thickness MeasureEventRowPadding()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        mainWindowViewModel.Groups[0].Events.Add(new EventEntry { Text = "One line", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        var listBox = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EventsListBox");
        var padding = listBox.GetVisualDescendants().OfType<ListBoxItem>().First().Padding;
        window.Close();
        return padding;
    }

    private static double MeasureEventRowHeight(string density)
    {
        DensityService.Apply(density);

        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        mainWindowViewModel.Groups[0].Events.Add(new EventEntry { Text = "One line", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        var listBox = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EventsListBox");
        var height = listBox.GetVisualDescendants().OfType<ListBoxItem>().First().Bounds.Height;
        window.Close();
        return height;
    }

    [Fact]
    public void SettingsDialog_RoundTripsDensity_AndKeepsThemePreference()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings { UiDensity = DensityService.Normal, ThemePreference = "Dark" });
        Assert.False(viewModel.CompactDensity);

        viewModel.CompactDensity = true;
        Assert.True(viewModel.TryBuildSettings(out var saved));

        Assert.Equal(DensityService.Compact, saved.UiDensity);
        // Regression: the dialog used to build a fresh AppSettings without the
        // theme, resetting it to "System" on every Save.
        Assert.Equal("Dark", saved.ThemePreference);
    }
}
