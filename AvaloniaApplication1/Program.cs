using Avalonia;
using System;
using Velopack;

namespace Archipolygo;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else - this is what lets Velopack
        // intercept the app's own install/update/uninstall hooks (e.g. a
        // freshly-applied update relaunching this same exe) before the real
        // Avalonia app ever starts. A no-op on every ordinary launch,
        // including every local `dotnet run`/`dotnet build` (see
        // Services/UpdateService.cs's own IsInstalled guard for the
        // update-check side of this - this call itself is always safe to
        // make, installed or not).
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}