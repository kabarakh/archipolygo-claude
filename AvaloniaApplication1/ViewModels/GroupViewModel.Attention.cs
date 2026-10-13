using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>Unread counter, badge and mute state for this server.</summary>
public partial class GroupViewModel
{
    /// <summary>
    /// Attention-worthy events since the group was last seen; owned by <see cref="Services.AttentionTracker"/>.
    /// The Dashboard deliberately doesn't count as seen. Never persisted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadAttention))]
    [NotifyPropertyChangedFor(nameof(DetachedWindowTitle))]
    [NotifyPropertyChangedFor(nameof(DetachedWindowBadgeCount))]
    private int _unreadAttentionCount;

    /// <summary>Pushed in by <see cref="MainWindowViewModel"/>; this view model has no settings access.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetachedWindowTitle))]
    private bool _showUnreadInTitle = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetachedWindowBadgeCount))]
    private bool _showUnreadBadge = true;

    public int DetachedWindowBadgeCount => ShowUnreadBadge ? UnreadAttentionCount : 0;

    public bool HasUnreadAttention => UnreadAttentionCount > 0;

    public bool IsNotificationsMuted => Group.NotificationsMuted;

    public string MuteMenuHeader => Group.NotificationsMuted ? "Unmute notifications" : "Mute notifications";

    private void RaiseMuteStateChanged()
    {
        OnPropertyChanged(nameof(IsNotificationsMuted));
        OnPropertyChanged(nameof(MuteMenuHeader));
    }

    /// <summary>"(3) Server" while something is unread, so it shows in Alt-Tab/taskbar too.</summary>
    public string DetachedWindowTitle => ShowUnreadInTitle && UnreadAttentionCount > 0
        ? $"({UnreadAttentionCount}) {HeaderText}"
        : HeaderText;
}
