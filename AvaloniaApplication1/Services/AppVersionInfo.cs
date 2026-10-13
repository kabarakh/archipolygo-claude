using System.Reflection;

namespace Archipolygo.Services;

/// <summary>
/// "dev" for local builds; release.yml overrides <c>InformationalVersion</c> with the release tag.
/// </summary>
public static class AppVersionInfo
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "dev";
}
