using System.Linq;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>Only a real mouse-wheel scroll disengages auto-follow; a row selection alone does not.</summary>
public class EventsListAutoScrollTests
{
    private static (MainWindow window, GroupViewModel group, ListBox listBox, ScrollViewer scrollViewer) SetUp()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        // Tall enough that the Auto-sized filter rows don't starve the star-sized list to zero height.
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 150; i++)
        {
            group.Events.Add(new EventEntry { Text = $"Event {i}", Type = EventType.Chat });
        }
        Dispatcher.UIThread.RunJobs();

        var listBox = (ListBox)window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EventsListBox");
        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().First();

        return (window, group, listBox, scrollViewer);
    }

    private static double DistanceFromBottom(ScrollViewer scrollViewer) =>
        scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;

    [AvaloniaFact]
    public void NewEvents_AutoScrollToBottom_WhenUserHasNotScrolledAway()
    {
        var (_, group, _, scrollViewer) = SetUp();

        Assert.True(DistanceFromBottom(scrollViewer) < 1.0,
            $"expected the list to already be scrolled to the newest event; distance from bottom={DistanceFromBottom(scrollViewer)}");

        group.Events.Add(new EventEntry { Text = "One more event", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        Assert.True(DistanceFromBottom(scrollViewer) < 1.0,
            $"expected auto-scroll to keep following the newest event; distance from bottom={DistanceFromBottom(scrollViewer)}");
    }

    [AvaloniaFact]
    public void NewEvents_DoNotForceScrollBack_AfterUserScrollsAwayWithTheMouseWheel()
    {
        var (window, group, listBox, scrollViewer) = SetUp();

        // Scroll up; MouseWheel's point is in window coordinates, so translate it.
        var localCenter = new Point(listBox.Bounds.Width / 2, listBox.Bounds.Height / 2);
        var pointOverList = listBox.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseWheel(pointOverList, new Vector(0, 3), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // The primary regression signal: without the wheel handler clearing _stickToBottom, the list snaps back and this fails.
        var distanceAfterWheelScroll = DistanceFromBottom(scrollViewer);
        Assert.True(distanceAfterWheelScroll > 1.0,
            "expected the wheel scroll to move the list away from the bottom and stay there");

        // Compare Offset.Y, not distance-from-bottom, which grows with every appended row anyway.
        var offsetYAfterWheelScroll = scrollViewer.Offset.Y;

        group.Events.Add(new EventEntry { Text = "Arrives after user scrolled away", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        Assert.True(System.Math.Abs(scrollViewer.Offset.Y - offsetYAfterWheelScroll) < 1.0,
            $"expected the scroll offset to stay where the user left it; " +
            $"offset.Y before new event={offsetYAfterWheelScroll}, after={scrollViewer.Offset.Y}");
    }

    /// <summary>
    /// Guards StyleKeyOverride: without it the control fell back to Button's bare template, which paints nothing and so
    /// failed hit-testing - clicks never reached it.
    /// </summary>
    [AvaloniaFact]
    public void JumpToNewestButton_AppearsWhenScrolledAway_AndJumpsBackOnClick()
    {
        var (window, _, listBox, scrollViewer) = SetUp();

        // Scoped to the ListBox's sibling: DashboardView's hidden but materialized JumpToNewestButton would make it ambiguous.
        var jumpButton = ((Panel)listBox.Parent!).Children.OfType<JumpToNewestButton>().Single();

        Assert.False(jumpButton.IsEffectivelyVisible, "hidden while still following the newest event.");

        var localCenter = new Point(listBox.Bounds.Width / 2, listBox.Bounds.Height / 2);
        var pointOverList = listBox.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseWheel(pointOverList, new Vector(0, 3), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(jumpButton.IsEffectivelyVisible, "expected the button to appear once scrolled away from the bottom.");

        var localCenterOfButton = new Point(jumpButton.Bounds.Width / 2, jumpButton.Bounds.Height / 2);
        var pointOverButton = jumpButton.TranslatePoint(localCenterOfButton, window) ?? localCenterOfButton;
        window.MouseDown(pointOverButton, MouseButton.Left);
        window.MouseUp(pointOverButton, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(jumpButton.IsEffectivelyVisible, "expected the button to hide itself again after jumping back to the bottom.");
        Assert.True(DistanceFromBottom(scrollViewer) < 1.0,
            $"expected the list back at the bottom; distance from bottom={DistanceFromBottom(scrollViewer)}");
    }
}
