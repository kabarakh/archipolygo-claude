using System.Linq;
using Avalonia;
using Avalonia.Themes.Fluent;

namespace Archipolygo.Services;

/// <summary>
/// Turns <see cref="Models.AppSettings.UiDensity"/> into the Fluent theme's
/// own <see cref="FluentTheme.DensityStyle"/> - see
/// Kompakteres-Layout.md, Teil A. Compact mainly shrinks
/// Fluent's <c>ListBoxItemPadding</c> (12,9,12,12 -> 4,2, which this app
/// loosens to 4,4 - see <see cref="CompactListBoxItemPadding"/>), which is what
/// made every Events/Hints row far taller than its text, plus the
/// Button/ComboBox/TextBox min heights in every filter row. Called at startup
/// alongside <see cref="ThemeService.Apply"/> (before the main window exists,
/// so no visible re-layout) and again whenever the Settings dialog is saved.
/// </summary>
public static class DensityService
{
    public const string Compact = "Compact";
    public const string Normal = "Normal";

    /// <summary>
    /// Fluent's own compact row padding (4,2) left consecutive Events/Hints/
    /// Items rows running into each other (dev feedback, 2026-09-26), so
    /// Compact overrides it with a little more vertical room. Set as an
    /// Application-level resource under Fluent's own key, which
    /// <see cref="Application.TryGetResource"/> checks before the theme's
    /// styles - one place, so every list's rows stay identically spaced
    /// (the Dashboard's Overview list zeroes its item padding out itself and
    /// is unaffected). Normal removes the override again and falls back to
    /// Fluent's own value.
    /// </summary>
    private const string ListBoxItemPaddingKey = "ListBoxItemPadding";
    private static readonly Thickness CompactListBoxItemPadding = new(4, 4);

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
        var isNormal = density == Normal;
        fluentTheme.DensityStyle = isNormal ? DensityStyle.Normal : DensityStyle.Compact;

        var resources = Application.Current!.Resources;
        if (isNormal)
        {
            resources.Remove(ListBoxItemPaddingKey);
        }
        else
        {
            resources[ListBoxItemPaddingKey] = CompactListBoxItemPadding;
        }
    }
}
