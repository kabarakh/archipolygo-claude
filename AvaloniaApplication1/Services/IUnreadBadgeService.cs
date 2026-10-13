using Avalonia.Controls;

namespace Archipolygo.Services;

/// <summary>
/// Native unread badges; each platform implements only the member matching its badge scope, the other no-ops.
/// Linux is left out: LauncherEntry needs an installed .desktop file the zip doesn't ship.
/// UI thread only; never throws.
/// </summary>
public interface IUnreadBadgeService
{
    /// <summary>Windows: overlay on this window's own taskbar button. 0 removes it.</summary>
    void SetWindowBadge(Window window, int count);

    /// <summary>macOS: the Dock icon's badge, one for the whole app. 0 removes it.</summary>
    void SetAppBadge(int count);
}
