using System;

namespace Archipolygo.Services;

/// <summary>
/// Taskbar-button/title-bar flash (see Feature-Plaene/Tab-Eigenes-Fenster.md,
/// Phase 2) - asks the OS to draw attention to whichever window currently
/// shows a group (resolved via <see cref="IGroupWindowLocator"/>), for a
/// hint, a DeathLink, or a progression item arriving while that window
/// isn't the active one.
/// </summary>
public interface IWindowAttentionService
{
    /// <summary>
    /// No-op if no window is registered for <paramref name="groupId"/>, or
    /// if it's already the active (focused) window - never throws (called
    /// from session event callbacks, which must never crash the app over a
    /// cosmetic attention request; see each platform implementation's own
    /// "best-effort" notes).
    /// </summary>
    void RequestAttention(Guid groupId);
}
