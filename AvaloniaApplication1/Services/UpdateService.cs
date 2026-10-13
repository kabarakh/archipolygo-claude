using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Archipolygo.Services;

/// <summary>Wraps Velopack's <see cref="UpdateManager"/> against this repo's GitHub Releases.</summary>
public class UpdateService : IUpdateService
{
    // GithubSource reads the Velopack assets (releases.{channel}.json), not the plain zips, from this repo's releases.
    private const string RepoUrl = "https://github.com/kabarakh/archipolygo-claude";

    private readonly UpdateManager? _updateManager;
    private readonly IDiagnosticLogger _diagnosticLogger;
    private UpdateInfo? _pendingUpdate;

    public bool IsManagedInstall => _updateManager?.IsInstalled ?? false;

    // release.yml produces no Velopack package for macOS (vpk can't cross-compile its codesign/productbuild steps).
    public bool SupportsManagedInstall => !OperatingSystem.IsMacOS();

    public UpdateService(IDiagnosticLogger? diagnosticLogger = null)
    {
        _diagnosticLogger = diagnosticLogger ?? NullDiagnosticLogger.Instance;

        try
        {
            // Public repo (anonymous rate limit is fine for one check per start); published releases only.
            var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
            _updateManager = new UpdateManager(source);
        }
        catch (Exception ex)
        {
            // Must never take the app down; every member guards against a null manager.
            _diagnosticLogger.Warning($"Failed to construct UpdateManager, update checks disabled for this session: {ex.Message}");
            _updateManager = null;
        }
    }

    public async Task<string?> CheckForUpdatesAsync()
    {
        // Unmanaged (local build or unzipped install): checking would just fail.
        if (_updateManager is null || !_updateManager.IsInstalled)
        {
            return null;
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            if (_pendingUpdate is not null)
            {
                _diagnosticLogger.Info($"Update check found version {_pendingUpdate.TargetFullRelease.Version}");
            }

            return _pendingUpdate?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            // Offline, unreachable feed, malformed release: treated as "no update", nothing the user can act on.
            _diagnosticLogger.Warning($"Update check failed, treating as up to date: {ex.Message}");
            _pendingUpdate = null;
            return null;
        }
    }

    public async Task DownloadAndApplyUpdateAsync()
    {
        if (_updateManager is null || !_updateManager.IsInstalled || _pendingUpdate is null)
        {
            return;
        }

        try
        {
            await _updateManager.DownloadUpdatesAsync(_pendingUpdate);

            // Restarts the app; only ever after the user's explicit "Update now", never unattended.
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _diagnosticLogger.Error($"Failed to download/apply update {_pendingUpdate.TargetFullRelease.Version}", ex);
            _pendingUpdate = null;
        }
    }
}
