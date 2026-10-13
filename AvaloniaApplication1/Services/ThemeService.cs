using Avalonia;
using Avalonia.Styling;

namespace Archipolygo.Services;

/// <summary>
/// Applied at startup before the main window exists, so there's no flash of the wrong variant.
/// </summary>
public static class ThemeService
{
    public static void Apply(string themePreference)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = themePreference switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>System -&gt; Light -&gt; Dark -&gt; System.</summary>
    public static string Next(string current) => current switch
    {
        "Light" => "Dark",
        "Dark" => "System",
        _ => "Light"
    };
}
