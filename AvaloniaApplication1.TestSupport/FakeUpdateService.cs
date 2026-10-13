using System.Threading.Tasks;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>No-Velopack stand-in: reports a pre-arranged version and records every call.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public bool IsManagedInstall { get; set; } = true;

    /// <summary>Defaults to true (every platform except macOS).</summary>
    public bool SupportsManagedInstall { get; set; } = true;

    /// <summary>Null means "no update available".</summary>
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
