using System.Linq;
using Avalonia;
using Avalonia.Themes.Fluent;

namespace Archipolygo.Services;

/// <summary>
/// Turns <see cref="Models.AppSettings.UiDensity"/> into the Fluent theme's
/// own <see cref="FluentTheme.DensityStyle"/> - see
/// Kompakteres-Layout.md, Teil A. Compact mainly shrinks
/// Fluent's <c>ListBoxItemPadding</c> (12,9,12,12 -> 4,2), which is what
/// made every Events/Hints row far taller than its text, plus the
/// Button/ComboBox/TextBox min heights in every filter row. Called at startup
/// alongside <see cref="ThemeService.Apply"/> (before the main window exists,
/// so no visible re-layout) and again whenever the Settings dialog is saved.
/// </summary>
public static class DensityService
{
    public const string Compact = "Compact";
    public const string Normal = "Normal";

    public static void Apply(string density)
    {
        var fluentTheme = Application.Current?.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluentTheme is null)
        {
            return;
        }

        // Anything other than an explicit "Normal" counts as Compact - matches
        // AppSettings.UiDensity's own default, so a hand-edited/garbled value
        // falls back to the same layout a fresh install gets.
        fluentTheme.DensityStyle = density == Normal ? DensityStyle.Normal : DensityStyle.Compact;
    }
}
