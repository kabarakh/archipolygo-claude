using System.Reflection;

namespace Archipolygo.Services;

/// <summary>
/// The app's version, shown in the UI (see <see cref="ViewModels.SettingsViewModel.AppVersion"/>).
/// Backed by <see cref="AssemblyInformationalVersionAttribute"/>, which the
/// SDK generates from the csproj's <c>InformationalVersion</c> MSBuild
/// property - "dev" for every local build (the csproj's own default), and
/// the actual release tag for a build published via release.yml, which
/// overrides that property on the `dotnet publish` command line. Read once
/// and cached: the running assembly's version can't change mid-process.
/// </summary>
public static class AppVersionInfo
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "dev";
}
