using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// The single entry point every "this deserves the user's attention"
/// trigger reports to (hint, DeathLink, item, chat - see
/// <see cref="AttentionCategory"/>). Decides, from the user's settings, the
/// server's mute flag and its own throttling, whether that turns into a
/// window blink - so none of the trigger sites have to know about any of
/// that. See the feature-plan archive's <c>Benachrichtigungen.md</c>.
/// </summary>
public interface IAttentionTracker
{
    /// <summary>
    /// Safe to call from any thread (session event callbacks) - the decision
    /// itself always runs on the UI thread. Callers only ever report live
    /// activity, never catch-up backlog.
    /// </summary>
    void Report(GroupViewModel group, AttentionCategory category);

    /// <summary>
    /// Resets <see cref="GroupViewModel.UnreadAttentionCount"/> for every
    /// group that's currently being looked at (see
    /// <see cref="IWindowAttentionService.IsGroupSeen"/>). Called on the UI
    /// thread whenever what's visible changes - tab selection, Dashboard
    /// toggle, detach/re-dock; window activation is picked up by the
    /// tracker itself.
    /// </summary>
    void RefreshSeenState();

    /// <summary>Takes effect for every later <see cref="Report"/> - called whenever the Settings dialog is saved.</summary>
    void ApplySettings(AppSettings settings);
}
