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

            // Seeds the real "Hint..." picker's Location mode (see
            // HintPickerViewModel, Feature-Plaene/Archiv/Hint-Eingabefeld.md)
            // with something to click through - Item mode needs no seeding
            // of its own, it already reads live off whatever ControlPanelWindow's
            // "Add item"/"Add hint" buttons feed into groupViewModel.ReceivedItems/Hints.
            connectionManager.SetHintableLocations(slot, new List<Archipolygo.Models.HintableLocation>
            {
                new() { LocationId = 1, Name = "Cave Entrance - Chest" },
                new() { LocationId = 2, Name = "Forest Clearing - Tree Stump" },
                new() { LocationId = 3, Name = "Mountain Pass - Summit Chest" },
                new() { LocationId = 4, Name = "Old Library - Basement" },
                new() { LocationId = 5, Name = "Village Square - Well" },
            });

            // Three more demo servers, deliberately not auto-connecting
            // (nothing here exercises their connection state) - purely so
            // there's more than one tab/Overview row to actually drag
            // around (see Feature-Plaene/Tab-Reihenfolge.md). "TestServer"
            // above stays first since ControlPanelWindow's buttons are all
            // wired to that one specifically.
            mainWindowViewModel.AddNewGroup("AlphaServer", "alpha.example", 38281, string.Empty, "AlphaSlot", autoConnect: false);
            mainWindowViewModel.AddNewGroup("BravoServer", "bravo.example", 38281, string.Empty, "BravoSlot", autoConnect: false);
            mainWindowViewModel.AddNewGroup("CharlieServer", "charlie.example", 38281, string.Empty, "CharlieSlot", autoConnect: false);

            // AddNewGroup above always selects whichever group it just added
            // - reselect TestServer so the app doesn't open on CharlieServer's
            // (empty) tab, and so the Dashboard's default view (see
            // MainWindowViewModel.IsDashboardVisible) shows all four rows
            // with TestServer's own data visible first if "Tab View" is
            // clicked.
            mainWindowViewModel.SelectedGroup = groupViewModel;

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

            // Same wiring as the real App.axaml.cs - without this, clicking
            // "Remove server" or hitting a password-needed slot here would
            // silently skip the dialog the real app shows, which isn't what
            // this harness is for (see .claude/skills/ui-feature-prototyp/SKILL.md -
            // it should behave like the real app minus the network).
            mainWindowViewModel.ShowPasswordPromptDialogAsync =
                (viewModel, cancellationToken) => PasswordPromptWindow.ShowDialogAsync(mainWindow, viewModel, cancellationToken);
            mainWindowViewModel.ShowConfirmationDialogAsync =
                viewModel => ConfirmationWindow.ShowDialogAsync(mainWindow, viewModel);

            desktop.MainWindow = mainWindow;

            var controlPanel = new ControlPanelWindow(groupViewModel, slot, siblingSlot, connectionManager);
            controlPanel.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
