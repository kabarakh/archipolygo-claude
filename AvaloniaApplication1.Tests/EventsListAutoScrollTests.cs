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

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the Events list's auto-scroll
/// behavior, now owned by <see cref="JumpToNewestButton"/> (originally
/// hand-built directly in <c>MainWindow.axaml.cs</c>, later factored out so
/// <c>DashboardView</c>'s own Events list could reuse the exact same
/// control instead of a second hand-copied implementation) - covers
/// <c>39d8a80</c>.
///
/// Named "UnlessUserHasSelection" in the plan, but the actual gate in the
/// code is a real mouse-wheel scroll over the list (<c>_stickToBottom</c>),
/// not merely having a row selected - <see cref="JumpToNewestButton"/>'s own
/// doc comment is explicit that only a wheel gesture is "the one
/// unambiguous signal that the user...wants to look at something else"; a
/// selection alone is actually cleared and overridden by the next
/// auto-scroll while <c>_stickToBottom</c> is still true. This test follows
/// the actual code, not the plan's shorthand name.
/// </summary>
public class EventsListAutoScrollTests
{
    private static (MainWindow window, GroupViewModel group, ListBox listBox, ScrollViewer scrollViewer) SetUp()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        // Matches MainWindow.axaml's own d:DesignWidth/Height (that attribute
        // only applies to the XAML previewer, not a real run, hence setting
        // it explicitly here) - tall enough that the several Auto-sized
        // filter/checkbox rows above the list don't themselves starve the
        // list's remaining (Grid.Row="3", star-sized) space down to zero.
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // This tests the tab's own content, which only materializes once
        // the TabControl is actually visible (see MainWindow.axaml's lazy
        // ContentTemplate gotcha, documented in CLAUDE.md) - the Dashboard
        // is the default view on startup, so switch away from it first.
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

        // A real wheel gesture over the list - the one thing that actually
        // disengages auto-follow (see the class doc comment). Scroll up
        // (positive Y delta) away from the bottom. MouseWheel's point is in
        // window coordinates, not the ListBox's own local space, so it needs
        // translating (same gotcha as a plain click - see RemoveConfiguredSlotButtonTests).
        var localCenter = new Point(listBox.Bounds.Width / 2, listBox.Bounds.Height / 2);
        var pointOverList = listBox.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseWheel(pointOverList, new Vector(0, 3), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // This is actually the primary regression signal, not a mere setup
        // assumption - verified by temporarily removing the
        // PointerWheelChangedEvent handler that sets _stickToBottom = false:
        // with it gone, JumpToNewestButton's own ScrollChanged handler
        // (still running with _stickToBottom stuck true) re-clears the
        // wheel's own scroll on the very next ScrollChanged, so the list
        // never visibly moves and this assertion is what actually fails.
        var distanceAfterWheelScroll = DistanceFromBottom(scrollViewer);
        Assert.True(distanceAfterWheelScroll > 1.0,
            "expected the wheel scroll to move the list away from the bottom and stay there");

        // Offset.Y itself (not "distance from bottom") is the right thing to
        // compare here: appending a new event grows Extent.Height by one
        // row's worth regardless of scroll position, so distance-from-bottom
        // necessarily grows too even when Offset correctly stayed put - that
        // growth is expected, not a sign of unwanted auto-scroll.
        var offsetYAfterWheelScroll = scrollViewer.Offset.Y;

        group.Events.Add(new EventEntry { Text = "Arrives after user scrolled away", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        // Must NOT have been forced back to the bottom by the new event.
        Assert.True(System.Math.Abs(scrollViewer.Offset.Y - offsetYAfterWheelScroll) < 1.0,
            $"expected the scroll offset to stay where the user left it; " +
            $"offset.Y before new event={offsetYAfterWheelScroll}, after={scrollViewer.Offset.Y}");
    }

    /// <summary>
    /// Clicking the button itself, not just scrolling away from/back to the
    /// bottom by hand - covers <see cref="JumpToNewestButton.StyleKeyOverride"/>:
    /// without it, the control silently fell back to <see cref="Button"/>'s
    /// bare pre-theme default template (no background/border chrome at all),
    /// which - since that template paints nothing over most of the button's
    /// own bounds - made it fail real hit-testing entirely; a click at its
    /// own center never reached it, so it never visibly reappeared/hid
    /// itself despite every property still updating correctly underneath.
    /// </summary>
    [AvaloniaFact]
    public void JumpToNewestButton_AppearsWhenScrolledAway_AndJumpsBackOnClick()
    {
        var (window, _, listBox, scrollViewer) = SetUp();

        // Scoped to the ListBox's own sibling, not window.GetVisualDescendants()
        // at large - MainWindow also hosts DashboardView's own (hidden, but
        // still materialized) JumpToNewestButton for its shared Events list,
        // which would otherwise make this ambiguous.
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
