using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// Stands in for real <see cref="Avalonia.Controls.Window"/>s so
/// <see cref="AttentionTracker"/>'s decision logic (settings, mute,
/// throttling) can be tested without any: each group resolves to a
/// <see cref="FakeWindow"/> token (one shared "main window" unless
/// <see cref="Detach"/> gives it its own), whose active state the test sets
/// directly - plus, for the main window, which group's tab is "on screen"
/// (<see cref="MainWindowShowingGroupId"/>; null = Dashboard or nothing).
/// Records every blink instead of calling the OS.
/// </summary>
public sealed class FakeWindowAttentionService : IWindowAttentionService
{
    public sealed class FakeWindow
    {
        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; set; }

        public override string ToString() => Name;
    }

    private readonly Dictionary<Guid, FakeWindow> _detached = new();

    public FakeWindow MainWindow { get; } = new() { Name = "main" };

    public Guid? MainWindowShowingGroupId { get; set; }

    public List<(FakeWindow Window, AttentionBlinkMode Mode, int Count)> Requests { get; } = new();

    public event Action<object>? WindowAcknowledged;

    public FakeWindow Detach(Guid groupId)
    {
        var window = new FakeWindow { Name = $"detached-{groupId}" };
        _detached[groupId] = window;
        return window;
    }

    /// <summary>Simulates the user focusing <paramref name="window"/> - what the real service forwards from <c>Window.Activated</c>.</summary>
    public void Activate(FakeWindow window)
    {
        window.IsActive = true;
        WindowAcknowledged?.Invoke(window);
    }

    public object? ResolveWindow(Guid groupId) => _detached.TryGetValue(groupId, out var window) ? window : MainWindow;

    public bool IsActive(object window) => ((FakeWindow)window).IsActive;

    public bool IsGroupSeen(Guid groupId)
    {
        var window = (FakeWindow)ResolveWindow(groupId)!;
        return window.IsActive && (window != MainWindow || MainWindowShowingGroupId == groupId);
    }

    public void RequestAttention(object window, AttentionBlinkMode mode, int blinkCount)
    {
        if (mode != AttentionBlinkMode.Off)
        {
            Requests.Add(((FakeWindow)window, mode, blinkCount));
        }
    }
}
