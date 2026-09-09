using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Archipolygo.Services;

/// <summary>
/// Real <see cref="IUpdateService"/> - wraps Velopack's own <see cref="UpdateManager"/>,
/// pointed at this repo's GitHub Releases (the same place release.yml
/// already publishes every build to). Package version pinned per CLAUDE.md's
/// rule: verified against Velopack 1.2.0's actual source at that tag
/// (ArchipelagoMW-style - not the locally installed DLL, not a newer/older
/// version's docs).
/// </summary>
public class UpdateService : IUpdateService
{
    // Matches this repo's own remote (see release.yml, which publishes every
    // build here) - GithubSource resolves the latest release's Velopack
    // assets (the .nupkg/releases.{channel}.json vpk pack produces) from
    // this URL, not the plain .zip downloads release.yml already attaches
    // for manual installs.
    private const string RepoUrl = "https://github.com/kabarakh/archipolygo-claude";

    private readonly UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;

    public bool IsManagedInstall => _updateManager?.IsInstalled ?? false;

    // release.yml only ever produces a Velopack package for win-x64/linux-x64 -
    // see that workflow's own doc comment for why macOS is excluded
    // (codesign/xcrun/productbuild are hard macOS-only dependencies vpk pack
    // can't satisfy by cross-compiling from the Linux runner it uses).
    public bool SupportsManagedInstall => !OperatingSystem.IsMacOS();

    public UpdateService()
    {
        try
        {
            // No access token (public repo, anonymous rate limits are fine
            // for one check per app start) and no prereleases (only real,
            // published releases - see release.yml's
            // "on: release: types: [published]").
            var source = new GithubSource(RepoUrl, accessToken: null, prerelease: false);
            _updateManager = new UpdateManager(source);
        }
        catch (Exception)
        {
            // Never let a construction-time failure here take the whole app
            // down with it - see this class's doc comment/CheckForUpdatesAsync's
            // "never throws" contract. _updateManager stays null; every
            // other member below already guards against that.
            _updateManager = null;
        }
    }

    public async Task<string?> CheckForUpdatesAsync()
    {
        // Not a Velopack-installed build at all (every local dotnet build/run,
        // and any manually-unzipped install from before this feature existed -
        // see Feature-Plaene/Archiv/Auto-Update.md's "migration path" note) - nothing
        // to check, and calling CheckForUpdatesAsync on an unmanaged install
        // would just fail anyway.
        if (_updateManager is null || !_updateManager.IsInstalled)
        {
            return null;
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            return _pendingUpdate?.TargetFullRelease.Version.ToString();
        }
        catch (Exception)
        {
            // Offline, feed unreachable, malformed release, ... - silently
            // treated the same as "no update available" rather than
            // surfacing an error the user can't act on. See this method's
            // doc comment.
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

            // Closes this process and relaunches the new version - the user
            // has already explicitly confirmed this via the "Update now"
            // button (see MainWindowViewModel.UpdateNowAsync), never
            // triggered automatically in the background while a connection
            // might be live. See Feature-Plaene/Archiv/Auto-Update.md's "explicitly
            // not part of this plan" note for why there's no unattended-
            // restart path here at all.
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
        }
        catch (Exception)
        {
            // Best-effort; a failed download/apply just leaves the app
            // running on its current version. Nothing left pending either
            // way - the next manual/startup check starts fresh.
            _pendingUpdate = null;
        }
    }
}
