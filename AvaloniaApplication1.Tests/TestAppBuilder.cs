using Archipolygo;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

// Reuses the real App (FluentTheme + DialogStyles) with UseHeadless; default PerTest isolation recreates the
// Application/Dispatcher per test so state can't leak between tests.
[assembly: AvaloniaTestApplication(typeof(AvaloniaApplication1.Tests.TestAppBuilder))]

namespace AvaloniaApplication1.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
