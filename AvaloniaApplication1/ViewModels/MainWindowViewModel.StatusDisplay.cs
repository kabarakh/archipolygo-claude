using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>The "Catching up slots" banner and the unread count in window titles/badges.</summary>
public partial class MainWindowViewModel
{
    /// <summary>Slots announced by in-flight sync passes - not only at startup, despite the name.</summary>
    [ObservableProperty]
    private int _startupSyncTotal;

    [ObservableProperty]
    private int _startupSyncCompleted;

    public bool IsStartupSyncing => StartupSyncTotal > 0 && StartupSyncCompleted < StartupSyncTotal;

    public string StartupSyncStatusText => $"Catching up slots: {StartupSyncCompleted}/{StartupSyncTotal}";

    /// <summary>
    /// Resets if nothing is pending, else extends the total - so a finished pass isn't carried
    /// over, but overlapping passes don't clobber each other.
    /// </summary>
    private void OnSlotSyncBatchStarting(int slotCount)
    {
        if (StartupSyncCompleted >= StartupSyncTotal)
        {
            StartupSyncTotal = 0;
            StartupSyncCompleted = 0;
        }

        StartupSyncTotal += slotCount;
    }

    partial void OnStartupSyncTotalChanged(int value)
    {
        OnPropertyChanged(nameof(IsStartupSyncing));
        OnPropertyChanged(nameof(StartupSyncStatusText));
    }

    partial void OnStartupSyncCompletedChanged(int value)
    {
        OnPropertyChanged(nameof(IsStartupSyncing));
        OnPropertyChanged(nameof(StartupSyncStatusText));
    }

    private readonly IAttentionTracker? _attentionTracker;

    private bool _showUnreadInTitle = true;

    private bool _showUnreadBadge = true;

    /// <summary>Counts docked groups only; detached ones count toward their own window's title.</summary>
    public string MainWindowTitle
    {
        get
        {
            var unread = DockedGroups.Sum(g => g.UnreadAttentionCount);
            return _showUnreadInTitle && unread > 0 ? $"({unread}) Archipolygo" : "Archipolygo";
        }
    }

    /// <summary>Windows taskbar badge: same docked-groups sum as <see cref="MainWindowTitle"/>.</summary>
    public int MainWindowBadgeCount => _showUnreadBadge ? DockedGroups.Sum(g => g.UnreadAttentionCount) : 0;

    /// <summary>macOS Dock badge: one per app, so every group counts, docked or detached.</summary>
    public int AppBadgeCount => _showUnreadBadge ? Groups.Sum(g => g.UnreadAttentionCount) : 0;

    private void ApplyUnreadDisplaySettings(AppSettings settings)
    {
        _showUnreadInTitle = settings.ShowUnreadInTitle;
        _showUnreadBadge = settings.ShowUnreadBadge;
        foreach (var group in Groups)
        {
            group.ShowUnreadInTitle = _showUnreadInTitle;
            group.ShowUnreadBadge = _showUnreadBadge;
        }

        RaiseUnreadDisplayChanged();
    }

    private void RaiseUnreadDisplayChanged()
    {
        OnPropertyChanged(nameof(MainWindowTitle));
        OnPropertyChanged(nameof(MainWindowBadgeCount));
        OnPropertyChanged(nameof(AppBadgeCount));
    }

    /// <summary>Watches <see cref="Groups"/>, not DockedGroups: every group feeds <see cref="AppBadgeCount"/>.</summary>
    private void OnGroupsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var group in e.OldItems?.OfType<GroupViewModel>() ?? Enumerable.Empty<GroupViewModel>())
        {
            group.PropertyChanged -= OnGroupUnreadPropertyChanged;
        }

        foreach (var group in e.NewItems?.OfType<GroupViewModel>() ?? Enumerable.Empty<GroupViewModel>())
        {
            group.PropertyChanged += OnGroupUnreadPropertyChanged;
        }

        RaiseUnreadDisplayChanged();
    }

    private void OnGroupUnreadPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupViewModel.UnreadAttentionCount))
        {
            RaiseUnreadDisplayChanged();
        }
    }
}
