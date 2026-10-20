using System;
using System.IO;

namespace Archipolygo.Services;

public enum PortableMarkerIgnoredReason
{
    // Velopack swaps the whole app folder on every update, so data next to the executable would be lost.
    ManagedInstall,

    // Gatekeeper/App Translocation make "next to the app" unreliable; not yet verified on real hardware.
    UnsupportedOperatingSystem,
}

/// <param name="IgnoredMarkerReason">Set when <c>portable.txt</c> exists but the portable mode still does not apply.</param>
public sealed record DataDirectory(string Path, bool IsPortable, PortableMarkerIgnoredReason? IgnoredMarkerReason);

/// <summary>The one place that decides where all app data (groups, sync state, settings, caches, logs) lives.</summary>
public static class DataDirectoryResolver
{
    public const string PortableMarkerFileName = "portable.txt";
    public const string PortableDataFolderName = "data";

    public static DataDirectory ResolveForCurrentProcess(bool isManagedInstall) =>
        Resolve(
            AppContext.BaseDirectory,
            CurrentUserAppDataRoot,
            isManagedInstall,
            OperatingSystem.IsMacOS());

    private static string CurrentUserAppDataRoot => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string AppDataDirectoryForCurrentUser() => AppDataDirectory(CurrentUserAppDataRoot);

    public static DataDirectory Resolve(string executableDirectory, string appDataRoot, bool isManagedInstall, bool isMacOS)
    {
        var appDataDirectory = new DataDirectory(AppDataDirectory(appDataRoot), IsPortable: false, IgnoredMarkerReason: null);

        if (!File.Exists(Path.Combine(executableDirectory, PortableMarkerFileName)))
        {
            return appDataDirectory;
        }

        if (isManagedInstall)
        {
            return appDataDirectory with { IgnoredMarkerReason = PortableMarkerIgnoredReason.ManagedInstall };
        }

        if (isMacOS)
        {
            return appDataDirectory with { IgnoredMarkerReason = PortableMarkerIgnoredReason.UnsupportedOperatingSystem };
        }

        return new DataDirectory(Path.Combine(executableDirectory, PortableDataFolderName), IsPortable: true, IgnoredMarkerReason: null);
    }

    private static string AppDataDirectory(string appDataRoot) => Path.Combine(appDataRoot, "Archipolygo");
}
