using System;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// The UI/platform half of window attention (taskbar-button/title-bar
/// flash, Dock bounce - see the feature-plan archive's
/// <c>Tab-Eigenes-Fenster.md</c>, Phase 2): resolves which window currently
/// shows a group (via <see cref="IGroupWindowLocator"/>), reports whether
/// it's active, and performs the actual OS call. Deciding *whether* to
/// blink (per-category settings, per-server mute, throttling) is
/// <see cref="IAttentionTracker"/>'s job, not this one's - see the
/// feature-plan archive's <c>Benachrichtigungen.md</c>.
///
/// Windows are exposed as opaque <see cref="object"/>s so the tracker's
/// decision logic stays testable without real <see cref="Avalonia.Controls.Window"/>
/// instances (see <c>FakeWindowAttentionService</c> in TestSupport). Every
/// member must be called on the UI thread.
/// </summary>
public interface IWindowAttentionService
{
    /// <summary>
    /// Raised when a window the user may have been asked to look at no
    /// longer needs to be - it was activated, or it closed. The tracker's
    /// throttling ("don't blink again until the user has looked") resets on
    /// this.
    /// </summary>
    event Action<object>? WindowAcknowledged;

    /// <summary>The window currently showing <paramref name="groupId"/>, or null if none is registered yet (e.g. too early at startup).</summary>
    object? ResolveWindow(Guid groupId);

    bool IsActive(object window);

    /// <summary>
    /// Whether the user is looking at <paramref name="groupId"/> right now:
    /// its window is active and actually shows it - for the main window,
    /// that means its tab is selected and the Dashboard isn't showing (the
    /// Dashboard deliberately doesn't count, see
    /// <see cref="ViewModels.GroupViewModel.UnreadAttentionCount"/>).
    /// </summary>
    bool IsGroupSeen(Guid groupId);

    /// <summary>
    /// Asks the OS to draw attention to <paramref name="window"/>. Never
    /// throws (called from session event paths, which must never crash the
    /// app over a cosmetic attention request); a no-op for
    /// <see cref="AttentionBlinkMode.Off"/>.
    /// </summary>
    void RequestAttention(object window, AttentionBlinkMode mode, int blinkCount);
}
