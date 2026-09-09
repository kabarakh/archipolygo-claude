using System.Threading.Tasks;

namespace Archipolygo.Services;

/// <summary>
/// Wraps Velopack's <c>UpdateManager</c> (see <see cref="UpdateService"/>) so
/// <see cref="ViewModels.MainWindowViewModel"/>/<see cref="ViewModels.SettingsViewModel"/>
/// never touch the Velopack API directly - same reasoning as <see cref="ISessionFactory"/>
/// for <c>Archipelago.MultiClient.Net</c>: this is the one seam, kept small
/// enough that AvaloniaApplication1.Tests can substitute a fake instead of a
/// real Velopack-installed build.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// True if this is a Velopack-managed install (installed via the
    /// packaged Setup.exe/.AppImage - see release.yml), as opposed to a
    /// manually downloaded/unzipped build. Drives whether the "you're on an
    /// unmanaged install" hint (see <see cref="ViewModels.SettingsViewModel"/>)
    /// makes sense to show at all - a manual/unzipped install can never
    /// auto-update no matter how many releases come out, since
    /// <see cref="CheckForUpdatesAsync"/> already silently no-ops for
    /// exactly this reason.
    /// </summary>
    bool IsManagedInstall { get; }

    /// <summary>
    /// Whether this platform could use a managed install at all. False on
    /// macOS, where release.yml never produces a Velopack package regardless
    /// of how the app was installed (see that workflow's own doc comment on
    /// why) - showing "download the installer instead" there would point at
    /// something that doesn't exist.
    /// </summary>
    bool SupportsManagedInstall { get; }

    /// <summary>
    /// Checks for an update and returns its version as a display string if
    /// one is available, else null - which also covers every reason this
    /// can't meaningfully check at all (not a Velopack-installed build, e.g.
    /// a local <c>dotnet run</c>; offline; the update feed unreachable).
    /// Never throws. The found update (if any) is remembered internally for
    /// <see cref="DownloadAndApplyUpdateAsync"/> to act on next.
    /// </summary>
    Task<string?> CheckForUpdatesAsync();

    /// <summary>
    /// Downloads whichever update <see cref="CheckForUpdatesAsync"/> last
    /// found, then applies it and restarts the app into the new version.
    /// No-op if nothing is pending (no prior successful check, or the app
    /// isn't a Velopack-installed build at all). Never throws - a failed
    /// download/apply just leaves the app running on its current version,
    /// same as if the user had never asked.
    /// </summary>
    Task DownloadAndApplyUpdateAsync();
}
