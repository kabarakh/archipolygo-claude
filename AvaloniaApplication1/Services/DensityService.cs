using System.Linq;
using Avalonia;
using Avalonia.Themes.Fluent;

namespace Archipolygo.Services;

/// <summary>
/// Maps <see cref="Models.AppSettings.UiDensity"/> onto Fluent's <see cref="FluentTheme.DensityStyle"/>.
/// Applied at startup before the main window exists, so there's no visible re-layout.
/// </summary>
public static class DensityService
{
    public const string Compact = "Compact";
    public const string Normal = "Normal";

    /// <summary>
    /// Fluent's compact padding (4,2) runs list rows into each other; an Application-level
    /// resource under Fluent's own key wins over the theme's styles.
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

        // Anything but "Normal" counts as Compact, matching AppSettings.UiDensity's default.
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
