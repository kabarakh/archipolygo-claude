using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Archipolygo.ViewModels;
using Archipolygo.Views;

namespace TestHarness;

public partial class TestHarnessApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var persistenceService = new FakePersistenceService();
            var connectionManager = new FakeConnectionManager();
            var mainWindowViewModel = new MainWindowViewModel(persistenceService, connectionManager);

            // One demo server with one slot, connected as leader right away
            // (see FakeConnectionManager.SwitchLeaderAsync) - enough to
            // exercise the Events/Hints/Items panels without any real
            // network activity.
            mainWindowViewModel.AddNewGroup("TestServer", "localhost", 38281, string.Empty, "TestSlot", autoConnect: true);
            var groupViewModel = mainWindowViewModel.Groups[0];
            var slot = groupViewModel.Group.Slots[0];

            // Explicit manual position so the main window can never overlap
            // ControlPanelWindow (which sits at (20,20), 260 wide) - see
            // .claude/skills/app-testen/SKILL.md on why the two must never
            // cover each other on screenshots.
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
                Position = new Avalonia.PixelPoint(300, 20)
            };
            desktop.MainWindow = mainWindow;

            var controlPanel = new ControlPanelWindow(groupViewModel, slot);
            controlPanel.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
