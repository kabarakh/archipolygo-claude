using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Archipolygo.Models;
using Archipolygo.TestSupport;
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
            var mainWindowViewModel = new MainWindowViewModel(persistenceService, connectionManager, new Archipolygo.Services.MultiworldTrackerService());

            // One demo server with two slots, connected as leader right away
            // (see FakeConnectionManager.SwitchLeaderAsync) - enough to
            // exercise the Events/Hints/Items panels without any real
            // network activity. The second slot exists specifically so
            // ControlPanelWindow's "Hint routing" section can demonstrate the
            // leader-vs-non-leader hint bug fixed in ConnectionManager.cs
            // (see FakeConnectionManager's fake hint room) - a single-slot
            // group can't show that scenario at all.
            mainWindowViewModel.AddNewGroup("TestServer", "localhost", 38281, string.Empty, "TestSlot", autoConnect: true);
            var groupViewModel = mainWindowViewModel.Groups[0];
            var slot = groupViewModel.Group.Slots[0];

            mainWindowViewModel.AddSlotsToGroup(groupViewModel, new List<StagedSlot>
            {
                new() { SlotName = "SiblingSlot", DisplayText = "SiblingSlot" }
            });
            var siblingSlot = groupViewModel.Group.Slots[1];

            // AddNewGroup's leader connect above already ran before this
            // sibling existed, so its subscription loop never saw it (same as
            // a real leader session that doesn't re-scan for new slots on its
            // own - see ConnectionManager.cs). Re-run the leader "connect" now
            // that both slots are configured, exactly as a real leader
            // reconnect/switch would, so the sibling's hint key actually gets
            // tracked.
            _ = connectionManager.SwitchLeaderAsync(groupViewModel, slot);

            // Explicit manual position so the main window can never overlap
            // ControlPanelWindow (which sits at (20,20), 260 wide) - keeps
            // both fully visible side by side for whoever is clicking
            // through this (see .claude/skills/ui-feature-prototyp/SKILL.md).
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
                Position = new Avalonia.PixelPoint(300, 20)
            };
            desktop.MainWindow = mainWindow;

            var controlPanel = new ControlPanelWindow(groupViewModel, slot, siblingSlot, connectionManager);
            controlPanel.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
