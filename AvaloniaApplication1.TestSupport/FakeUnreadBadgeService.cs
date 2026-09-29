using System.Collections.Generic;
using Archipolygo.Services;
using Avalonia.Controls;

namespace Archipolygo.TestSupport;

/// <summary>Records the last badge set per window and for the app, instead of touching the real taskbar/Dock - see <see cref="IUnreadBadgeService"/>.</summary>
public sealed class FakeUnreadBadgeService : IUnreadBadgeService
{
    public Dictionary<Window, int> WindowBadges { get; } = new();

    public int? AppBadge { get; private set; }

    public void SetWindowBadge(Window window, int count) => WindowBadges[window] = count;

    public void SetAppBadge(int count) => AppBadge = count;
}
