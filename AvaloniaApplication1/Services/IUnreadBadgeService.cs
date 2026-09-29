using Avalonia.Controls;

namespace Archipolygo.Services;

/// <summary>
/// Native unread badges (the feature-plan archive's <c>Benachrichtigungen.md</c>,
/// step 4) - the same number as the window title's "(3)", shown where the
/// OS shows app badges. Each platform only implements the member that
/// matches how it scopes badges; the other is a no-op there. Linux was
/// deliberately left out (its LauncherEntry badge needs an installed
/// .desktop file the release zip doesn't ship) - the title count covers it.
/// Every member must be called on the UI thread and never throws.
/// </summary>
public interface IUnreadBadgeService
{
    /// <summary>Windows: overlay icon on <paramref name="window"/>'s own taskbar button (each detached window has its own). 0 removes it.</summary>
    void SetWindowBadge(Window window, int count);

    /// <summary>macOS: the Dock icon's badge, one for the whole app. 0 removes it.</summary>
    void SetAppBadge(int count);
}
