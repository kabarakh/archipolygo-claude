using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the Events/Hints panels sit the same
/// distance from the window's left/right/bottom edge in a MainWindow tab, on
/// the Dashboard and in a <see cref="DetachedGroupWindow"/>. A tab's content
/// gets an extra inset from Fluent's own <c>TabControl.Padding</c>
/// (<c>TabItemMargin</c>) that the other two hosts used to lack, so their
/// panels sat visibly closer to the window edge.
/// </summary>
public class ContentInsetTests
{
    private const double WindowWidth = 1200;
    private const double WindowHeight = 600;

    /// <summary>Left, right and bottom distance from <paramref name="window"/>'s edges to <paramref name="view"/>'s root layout Grid (the element carrying the view's own Margin="12").</summary>
    private static (double Left, double Right, double Bottom) InsetOf(Window window, UserControl view)
    {
        var rootGrid = (Control)view.Content!;
        var topLeft = rootGrid.TranslatePoint(new Point(0, 0), window)!.Value;
        var bottomRight = rootGrid.TranslatePoint(new Point(rootGrid.Bounds.Width, rootGrid.Bounds.Height), window)!.Value;
        return (topLeft.X, window.Bounds.Width - bottomRight.X, window.Bounds.Height - bottomRight.Y);
    }

    [AvaloniaFact]
    public void DashboardAndDetachedWindow_UseTheSameEdgeInsetAsATab()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var mainWindow = new MainWindow { DataContext = mainWindowViewModel, Width = WindowWidth, Height = WindowHeight };
        mainWindow.Show();
        Dispatcher.UIThread.RunJobs();

        mainWindowViewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();
        var dashboardInset = InsetOf(mainWindow, mainWindow.GetVisualDescendants().OfType<DashboardView>().Single());

        mainWindowViewModel.IsDashboardVisible = false;
        mainWindowViewModel.SelectedGroup = mainWindowViewModel.Groups[0];
        Dispatcher.UIThread.RunJobs();
        var tabInset = InsetOf(mainWindow, mainWindow.GetVisualDescendants().OfType<GroupDetailView>().Single(v => v.IsEffectivelyVisible));

        var detachedWindow = new DetachedGroupWindow { DataContext = mainWindowViewModel.Groups[0], Width = WindowWidth, Height = WindowHeight };
        detachedWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var detachedInset = InsetOf(detachedWindow, detachedWindow.GetVisualDescendants().OfType<GroupDetailView>().Single());

        Assert.Equal(tabInset, dashboardInset);
        Assert.Equal(tabInset, detachedInset);

        detachedWindow.Close();
        mainWindow.Close();
    }
}
