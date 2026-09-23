using Avalonia;
using Avalonia.Styling;

namespace Archipolygo.Services;

/// <summary>
/// Turns <see cref="Models.AppSettings.ThemePreference"/> into an actual
/// applied <see cref="Application.RequestedThemeVariant"/>, and cycles it for
/// the theme button in MainWindow.axaml - see
/// Feature-Plaene/Theme-Umschalter-und-Server-Farbwaehler.md. Called both at
/// startup (<see cref="App"/>, before the main window is even created, so
/// there's no visible flash of the wrong variant) and from
/// <see cref="ViewModels.MainWindowViewModel.CycleThemeCommand"/>.
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

    /// <summary>System -&gt; Light -&gt; Dark -&gt; System -&gt; ... - what each click on the theme button advances to.</summary>
    public static string Next(string current) => current switch
    {
        "Light" => "Dark",
        "Dark" => "System",
        _ => "Light"
    };
}
