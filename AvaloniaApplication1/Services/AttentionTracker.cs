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

    /// <summary>Windows that blinked and weren't looked at since: no second blink until then. UI-thread only.</summary>
    private readonly HashSet<object> _pendingWindows = new(ReferenceEqualityComparer.Instance);

    /// <summary>Groups with a raised <see cref="GroupViewModel.UnreadAttentionCount"/>. UI-thread only.</summary>
    private readonly HashSet<GroupViewModel> _unreadGroups = new();

    /// <summary>Groups whose watched-slot strip has rows marked new. UI-thread only.</summary>
    private readonly HashSet<GroupViewModel> _groupsWithNewWatchedItems = new();

    private AppSettings _settings;

    private readonly IUiUpdateQueue _updateQueue;

    public AttentionTracker(IWindowAttentionService windows, IPersistenceService persistenceService, IUiUpdateQueue? updateQueue = null)
    {
        _windows = windows;
        _updateQueue = updateQueue ?? UiUpdateQueue.Shared;
        _settings = persistenceService.LoadSettings();
        _windows.WindowAcknowledged += OnWindowAcknowledged;
    }

    public void ApplySettings(AppSettings settings) => Dispatcher.UIThread.Post(() => _settings = settings.Clone());

    /// <summary>Through the update queue, so a burst of item reports is time-sliced like the items themselves.</summary>
    public void Report(GroupViewModel group, AttentionCategory category) =>
        _updateQueue.Enqueue(group, () => ReportOnUiThread(group, category));

    public void NoteNewWatchedItems(GroupViewModel group)
    {
        if (_windows.IsGroupSeen(group.Group.Id))
        {
            group.MarkWatchedItemsSeen();
            return;
        }

        _groupsWithNewWatchedItems.Add(group);
    }

    public void RefreshSeenState()
    {
        foreach (var group in _groupsWithNewWatchedItems.Where(group => _windows.IsGroupSeen(group.Group.Id)).ToList())
        {
            group.MarkWatchedItemsSeen();
            _groupsWithNewWatchedItems.Remove(group);
        }

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

        // Avalonia's WindowBase raises Activated before setting IsActive; deferring lets IsGroupSeen see it active.
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

        // An active window showing another tab still counts, but doesn't blink.
        var window = _windows.ResolveWindow(group.Group.Id);
        if (window is null || _windows.IsActive(window) || !_pendingWindows.Add(window))
        {
            return;
        }

        _windows.RequestAttention(window, _settings.BlinkMode, _settings.AttentionBlinkCount);
    }
}
