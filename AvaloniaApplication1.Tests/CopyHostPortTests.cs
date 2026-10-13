using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>Kategorie C: copying "host:port" from the tab's status row and the Dashboard row's context menu.</summary>
public class CopyHostPortTests
{
    private static (MainWindow window, MainWindowViewModel viewModel) SetUp()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "archipelago.gg", 38281, string.Empty, "Alice", autoConnect: false);

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return (window, mainWindowViewModel);
    }

    private static Control ShowTabAndFind(MainWindow window, MainWindowViewModel viewModel, string name)
    {
        // Tab content only materializes once visible (lazy ContentTemplate).
        viewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
    }

    private static void Click(MainWindow window, Control control)
    {
        var localCenter = new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var pointInWindow = control.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<string?> ClipboardText(MainWindow window) =>
        await TopLevel.GetTopLevel(window)!.Clipboard!.TryGetTextAsync();

    [AvaloniaFact]
    public async Task ClickingTheHostPortText_CopiesHostPort()
    {
        var (window, viewModel) = SetUp();
        var text = ShowTabAndFind(window, viewModel, "HostPortText");

        Click(window, text);

        Assert.Equal("archipelago.gg:38281", await ClipboardText(window));
        Assert.True(ShowTabAndFind(window, viewModel, "HostPortCopiedText").IsVisible, "expected the brief 'Copied' confirmation");
    }

    [AvaloniaFact]
    public async Task ClickingTheCopyIcon_CopiesHostPort()
    {
        var (window, viewModel) = SetUp();
        var button = ShowTabAndFind(window, viewModel, "CopyHostPortButton");

        Click(window, button);

        Assert.Equal("archipelago.gg:38281", await ClipboardText(window));
    }

    [AvaloniaFact]
    public async Task DashboardContextMenu_CopyHostPort_CopiesThatRowsHostPort()
    {
        var (window, _) = SetUp();

        // The menu items only get the row's GroupViewModel as DataContext once the menu is open.
        var contextMenu = window.GetVisualDescendants().OfType<Border>()
            .Select(b => (border: b, menu: b.ContextMenu))
            .First(x => x.menu is not null && x.menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "Copy host:port")));
        contextMenu.menu!.Open(contextMenu.border);
        Dispatcher.UIThread.RunJobs();
        var menuItem = contextMenu.menu.Items.OfType<MenuItem>().First(i => Equals(i.Header, "Copy host:port"));

        menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("archipelago.gg:38281", await ClipboardText(window));
    }
}
