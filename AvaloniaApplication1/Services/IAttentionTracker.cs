using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Single entry point for every attention trigger; applies settings, per-server mute and
/// throttling so trigger sites don't have to.
/// </summary>
public interface IAttentionTracker
{
    /// <summary>
    /// Safe from any thread; the decision runs on the UI thread. Report live activity only, never catch-up backlog.
    /// </summary>
    void Report(GroupViewModel group, AttentionCategory category);

    /// <summary>
    /// Resets <see cref="GroupViewModel.UnreadAttentionCount"/> for every group currently on screen.
    /// UI thread only; call when visibility changes (window activation is tracked internally).
    /// </summary>
    void RefreshSeenState();

    /// <summary>Affects only later <see cref="Report"/> calls.</summary>
    void ApplySettings(AppSettings settings);
}
