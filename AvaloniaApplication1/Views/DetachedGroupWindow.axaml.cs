using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

/// <summary>A single group in its own window, reusing the same <see cref="GroupDetailView"/> as a docked tab.</summary>
public partial class DetachedGroupWindow : Window, IGroupHostWindow
{
    public bool IsShowingGroup(Guid groupId) => _group?.Group.Id == groupId;

    private GroupViewModel? _group;
    private Action<GroupViewModel>? _redockRequested;

    public DetachedGroupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Deregisters from <paramref name="locator"/> on any close (programmatic redock or user close), so
    /// attention flashes never resolve to a dead window. The badge is re-applied on Opened/ScalingChanged
    /// because Win32 clears taskbar overlays on DPI change.
    /// </summary>
    public static DetachedGroupWindow Create(GroupViewModel group, IGroupWindowLocator locator, Action<GroupViewModel> redockRequested,
                                             IUnreadBadgeService? unreadBadge = null)
    {
        var window = new DetachedGroupWindow { DataContext = group, _group = group, _redockRequested = redockRequested };

        locator.RegisterDetachedWindow(group.Group.Id, window);
        window.Closed += (_, _) => locator.UnregisterDetachedWindow(group.Group.Id);

        if (unreadBadge is not null)
        {
            void Apply() => unreadBadge.SetWindowBadge(window, group.DetachedWindowBadgeCount);

            void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(GroupViewModel.DetachedWindowBadgeCount))
                {
                    Apply();
                }
            }

            group.PropertyChanged += OnGroupPropertyChanged;
            window.Opened += (_, _) => Apply();
            window.ScalingChanged += (_, _) => Apply();
            window.Closed += (_, _) => group.PropertyChanged -= OnGroupPropertyChanged;
        }

        return window;
    }

    // Doesn't close itself: RedockGroup closes this window via CloseDetachedWindow.
    private void OnDockToMainWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_group is { } group)
        {
            _redockRequested?.Invoke(group);
        }
    }
}
