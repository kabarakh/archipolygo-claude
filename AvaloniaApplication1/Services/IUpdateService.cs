using System.Threading.Tasks;

namespace Archipolygo.Services;

/// <summary>The only seam to Velopack's <c>UpdateManager</c>, so tests can substitute a fake.</summary>
public interface IUpdateService
{
    /// <summary>False for a manually unzipped build, which can never auto-update.</summary>
    bool IsManagedInstall { get; }

    /// <summary>False on macOS: release.yml produces no Velopack package there.</summary>
    bool SupportsManagedInstall { get; }

    /// <summary>
    /// The available version, or null (also when unmanaged, offline, or feed unreachable). Never throws.
    /// Remembers the found update for <see cref="DownloadAndApplyUpdateAsync"/>.
    /// </summary>
    Task<string?> CheckForUpdatesAsync();

    /// <summary>
    /// Downloads and applies the update from the last check, then restarts. No-op if none pending;
    /// never throws - a failure leaves the current version running.
    /// </summary>
    Task DownloadAndApplyUpdateAsync();
}
