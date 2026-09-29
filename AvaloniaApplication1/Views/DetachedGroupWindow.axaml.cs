using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace Archipolygo.Views;

/// <summary>
/// A single group living in its own window, detached from MainWindow's tab
/// strip via its "Open in new window" context-menu item - see
/// Feature-Plaene/Tab-Eigenes-Fenster.md. Deliberately shows exactly one
/// group (no mini tab strip of its own, per that plan's decided scope) -
/// its content is the exact same <see cref="GroupDetailView"/> a docked tab
/// uses, so this window has no Events/Hints/Items logic of its own to keep
/// in sync.
/// </summary>
public partial class DetachedGroupWindow : Window, IGroupHostWindow
{
    /// <summary>Always true for its one group - this window shows nothing else.</summary>
    public bool IsShowingGroup(Guid groupId) => _group?.Group.Id == groupId;

    private GroupViewModel? _group;
    private Action<GroupViewModel>? _redockRequested;

    public DetachedGroupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Registers <paramref name="group"/> with <paramref name="locator"/> (so
    /// <see cref="IWindowAttentionService"/> flashes this window instead of
    /// MainWindow while it's the one showing this group) and deregisters it
    /// again once this window closes, for any reason - the "Dock to main
    /// window" button closes it programmatically (see MainWindow.axaml.cs),
    /// a plain user close does not, but either way nothing should keep
    /// resolving to a window that no longer exists.
    /// </summary>
    /// <param name="unreadBadge">
    /// Optional (Benachrichtigungen.md step 4): keeps this window's own
    /// Windows taskbar overlay in sync with <see cref="GroupViewModel.DetachedWindowBadgeCount"/> -
    /// re-applied on Opened/ScalingChanged for the same reasons as
    /// MainWindow's own badge.
    /// </param>
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

    /// <summary>
    /// "Dock to main window" - calls back into <see cref="MainWindowViewModel.RedockGroup"/>
    /// (see <see cref="Create"/>'s <c>redockRequested</c> parameter), which
    /// itself closes this window via <see cref="MainWindowViewModel.CloseDetachedWindow"/>
    /// - this button doesn't close the window directly.
    /// </summary>
    private void OnDockToMainWindowClick(object? sender, RoutedEventArgs e)
    {
        if (_group is { } group)
        {
            _redockRequested?.Invoke(group);
        }
    }
}
