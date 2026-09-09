using System.Threading.Tasks;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// No-Velopack stand-in for <see cref="IUpdateService"/> - Feature-Plaene/Archiv/Auto-Update.md.
/// A test arranges whatever version <see cref="CheckForUpdatesAsync"/> should
/// report up front (or leaves it null for "no update available"); every call
/// is also recorded so a test can assert
/// <see cref="ViewModels.MainWindowViewModel"/>'s reaction to it (the dot
/// next to "Settings...", the Flyout's version text) without a real
/// Velopack-installed build.
/// </summary>
public sealed class FakeUpdateService : IUpdateService
{
    /// <summary>Settable - defaults to true (the common case a test cares about: a real managed install).</summary>
    public bool IsManagedInstall { get; set; } = true;

    /// <summary>Settable - defaults to true (every platform except macOS).</summary>
    public bool SupportsManagedInstall { get; set; } = true;

    /// <summary>What the next <see cref="CheckForUpdatesAsync"/> call should report - null means "no update available".</summary>
    public string? NextCheckResult { get; set; }

    public int CheckForUpdatesCallCount { get; private set; }

    public int DownloadAndApplyUpdateCallCount { get; private set; }

    public Task<string?> CheckForUpdatesAsync()
    {
        CheckForUpdatesCallCount++;
        return Task.FromResult(NextCheckResult);
    }

    public Task DownloadAndApplyUpdateAsync()
    {
        DownloadAndApplyUpdateCallCount++;
        return Task.CompletedTask;
    }
}
