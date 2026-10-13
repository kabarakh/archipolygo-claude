using System;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Platform half of window attention (resolve window, is-active, the OS flash/bounce call);
/// <see cref="IAttentionTracker"/> decides whether to blink. Windows are opaque objects so that
/// logic is testable without real windows. UI thread only.
/// </summary>
public interface IWindowAttentionService
{
    /// <summary>A window was activated or closed; resets the tracker's "don't blink again until looked at" throttle.</summary>
    event Action<object>? WindowAcknowledged;

    /// <summary>Null if none is registered yet (e.g. early at startup).</summary>
    object? ResolveWindow(Guid groupId);

    bool IsActive(object window);

    /// <summary>
    /// Window active and actually showing the group (tab selected); the Dashboard deliberately doesn't count.
    /// </summary>
    bool IsGroupSeen(Guid groupId);

    /// <summary>
    /// Never throws (called from session event paths); no-op for <see cref="AttentionBlinkMode.Off"/>.
    /// </summary>
    void RequestAttention(object window, AttentionBlinkMode mode, int blinkCount);
}
