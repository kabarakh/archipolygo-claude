using Archipolygo;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

// One-time assembly-wide wiring for [AvaloniaFact]/[AvaloniaTheory] (see
// Test-Umsetzungsplan.md, Kategorie C). Reuses the real app's own App class
// (App.axaml already declares FluentTheme + DialogStyles, so those apply here
// exactly as in production) instead of a separate throwaway test App - the
// only difference from Program.cs's BuildAvaloniaApp is UseHeadless(...)
// instead of UsePlatformDetect(). PerTest isolation (the default - not
// overridden with [assembly: AvaloniaTestIsolation(PerAssembly)]) recreates
// the Application/Dispatcher for every single test, so tests can't leak state
// into each other via shared Application-level styles/resources.
[assembly: AvaloniaTestApplication(typeof(AvaloniaApplication1.Tests.TestAppBuilder))]

namespace AvaloniaApplication1.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
