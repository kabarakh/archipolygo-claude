using System.Collections.Generic;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <inheritdoc cref="IAttentionTracker"/>
public sealed class AttentionTracker : IAttentionTracker
{
    private readonly IWindowAttentionService _windows;

    /// <summary>
    /// Windows that already blinked and haven't been looked at since.
    /// Throttling rule (Benachrichtigungen.md): no second blink until the user
    /// has acknowledged the first one - otherwise five progression items in a
    /// row meant five back-to-back blinks. Deliberately a fixed rule, not a
    /// setting. UI-thread only.
    /// </summary>
    private readonly HashSet<object> _pendingWindows = new(ReferenceEqualityComparer.Instance);

    /// <summary>Groups whose <see cref="GroupViewModel.UnreadAttentionCount"/> this tracker raised above 0 - the only ones <see cref="RefreshSeenState"/> has to check. UI-thread only.</summary>
    private readonly HashSet<GroupViewModel> _unreadGroups = new();

    private AppSettings _settings;

    public AttentionTracker(IWindowAttentionService windows, IPersistenceService persistenceService)
    {
        _windows = windows;
        _settings = persistenceService.LoadSettings();
        _windows.WindowAcknowledged += OnWindowAcknowledged;
    }

    public void ApplySettings(AppSettings settings) => Dispatcher.UIThread.Post(() => _settings = settings.Clone());

    public void Report(GroupViewModel group, AttentionCategory category) =>
        Dispatcher.UIThread.Post(() => ReportOnUiThread(group, category));

    public void RefreshSeenState()
    {
        foreach (var group in _unreadGroups.ToList())
        {
            if (group.UnreadAttentionCount == 0 || _windows.IsGroupSeen(group.Group.Id))
            {
                group.UnreadAttentionCount = 0;
                _unreadGroups.Remove(group);
            }
        }
    }

    private void OnWindowAcknowledged(object window)
    {
        _pendingWindows.Remove(window);

        // Avalonia 12.0.4's WindowBase raises Activated *before* setting
        // IsActive (see its HandleActivated) - deferring lets IsGroupSeen
        // see the window as active.
        Dispatcher.UIThread.Post(RefreshSeenState);
    }

    private void ReportOnUiThread(GroupViewModel group, AttentionCategory category)
    {
        if (group.Group.NotificationsMuted)
        {
            return;
        }

        var categorySetting = _settings.GetAttentionCategorySetting(category);
        if (!categorySetting.Count && !categorySetting.Blink)
        {
            return;
        }

        // Looking right at it: neither count nor blink.
        if (_windows.IsGroupSeen(group.Group.Id))
        {
            return;
        }

        if (categorySetting.Count)
        {
            group.UnreadAttentionCount++;
            _unreadGroups.Add(group);
        }

        if (!categorySetting.Blink || _settings.BlinkMode == AttentionBlinkMode.Off)
        {
            return;
        }

        // An active window whose other tab (or Dashboard) is showing still
        // counts above, but the user is already at the window - no blink.
        var window = _windows.ResolveWindow(group.Group.Id);
        if (window is null || _windows.IsActive(window) || !_pendingWindows.Add(window))
        {
            return;
        }

        _windows.RequestAttention(window, _settings.BlinkMode, _settings.AttentionBlinkCount);
    }
}
