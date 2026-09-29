using System.Collections.Generic;
using System.Linq;
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

            // Real attention pipeline (Benachrichtigungen.md) - the same
            // tracker/platform layer as App.axaml.cs, so ControlPanelWindow's
            // "Notifications" buttons really blink this harness's window and
            // update its tab badge/title.
            var groupWindowLocator = new Archipolygo.Services.GroupWindowLocator();
            var attentionTracker = new Archipolygo.Services.AttentionTracker(
                new Archipolygo.Services.WindowAttentionService(groupWindowLocator), persistenceService);
            // Fake tracker so the Admin view (Admin-Funktionen.md) has status/
            // checks/inactivity to show - see SeedAdminDemo below.
            var trackerService = new FakeMultiworldTrackerService();
            var mainWindowViewModel = new MainWindowViewModel(persistenceService, connectionManager, trackerService,
                attentionTracker: attentionTracker);

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

            SeedAdminDemo(groupViewModel, connectionManager, trackerService);

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
            groupWindowLocator.RegisterMainWindow(mainWindow);
            mainWindow.GroupWindowLocator = groupWindowLocator;
            mainWindow.UnreadBadgeService = new Archipolygo.Services.UnreadBadgeService();
            mainWindowViewModel.OpenDetachedWindow = mainWindow.OpenDetachedGroupWindow;
            mainWindowViewModel.CloseDetachedWindow = mainWindow.CloseDetachedGroupWindowIfOpen;

            var controlPanel = new ControlPanelWindow(groupViewModel, slot, siblingSlot, connectionManager, attentionTracker);
            controlPanel.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Admin-Funktionen.md: TestServer is marked "I'm the admin" and gets a
    /// fake room roster, fake tracker data (status/checks/last activity),
    /// DataPackage names per game and room settings. The fake admin server
    /// in FakeConnectionManager accepts the password "admin".
    /// </summary>
    private static void SeedAdminDemo(GroupViewModel group, FakeConnectionManager connectionManager, FakeMultiworldTrackerService trackerService)
    {
        const string trackerId = "demo-tracker";
        group.Group.IsAdmin = true;
        group.Group.TrackerId = trackerId;

        var now = System.DateTimeOffset.UtcNow;
        var own1 = group.Group.Slots[0].SlotName;
        var own2 = group.Group.Slots.Count > 1 ? group.Group.Slots[1].SlotName : "SiblingSlot";
        var players = new (int Slot, string Name, string? Alias, string Game, int Status, int Done, int Total, System.TimeSpan? Inactive)[]
        {
            (1, own1, null, "A Link to the Past", 20, 142, 216, System.TimeSpan.FromMinutes(4)),
            (2, own2, null, "Hollow Knight", 20, 88, 190, System.TimeSpan.FromHours(7)),
            (3, "Mira", "Mira the Brave", "Super Metroid", 20, 61, 100, System.TimeSpan.FromDays(1.2)),
            (4, "Player One", null, "A Link to the Past", 0, 203, 216, System.TimeSpan.FromDays(3.4)),
            (5, "Kestrel", null, "Hollow Knight", 0, 12, 190, System.TimeSpan.FromDays(5.8)),
            (6, "Done Dan", null, "Super Metroid", 30, 100, 100, System.TimeSpan.FromDays(2.1)),
            (7, "Latecomer", null, "Hollow Knight", 0, 0, 190, null),
            (8, "Nightowl", null, "A Link to the Past", 5, 177, 216, System.TimeSpan.FromSeconds(40)),
        };

        var games = new Dictionary<string, (string[] Items, string[] Locations)>
        {
            ["A Link to the Past"] = (
                ["Progressive Sword", "Hookshot", "Pegasus Boots", "Magic Mirror", "Moon Pearl", "Hammer", "Fire Rod", "Flippers", "Bombos", "Rupees (20)"],
                ["Link's House", "Sanctuary", "Eastern Palace - Big Chest", "Desert Palace - Torch", "Tower of Hera - Big Chest", "Kakariko Well - Top", "Lumberjack Tree", "Spectacle Rock", "Ether Tablet", "Master Sword Pedestal"]),
            ["Hollow Knight"] = (
                ["Mothwing Cloak", "Mantis Claw", "Crystal Heart", "Monarch Wings", "Isma's Tear", "Shade Cloak", "Dream Nail", "Simple Key", "Pale Ore", "Grub"],
                ["King's Pass", "Greenpath - Hornet", "Fungal Wastes - Mantis Claw", "City of Tears - Lemm", "Crystal Peak - Crystal Heart", "Resting Grounds - Dream Nail", "Deepnest - Nosk", "Ancient Basin - Monarch Wings", "Queen's Gardens - Love Key", "Royal Waterways - Isma's Tear"]),
            ["Super Metroid"] = (
                ["Morph Ball", "Bomb", "Varia Suit", "Gravity Suit", "Speed Booster", "Grapple Beam", "Space Jump", "Screw Attack", "Missile", "Energy Tank"],
                ["Morphing Ball", "Missile (Crateria bottom)", "Bomb", "Energy Tank, Brinstar Ceiling", "Varia Suit", "Speed Booster", "Grapple Beam", "Gravity Suit", "Space Jump", "Screw Attack"]),
        };

        var locationIds = new Dictionary<string, Dictionary<string, long>>();
        long nextId = 1000;
        foreach (var (game, data) in games)
        {
            locationIds[game] = data.Locations.ToDictionary(name => name, _ => nextId++);
            connectionManager.SetGameData(game, new GameDataNames { ItemNames = data.Items, LocationIds = locationIds[game] });
        }

        connectionManager.RoomPlayers = players
            .Select(p => new Archipelago.MultiClient.Net.Helpers.PlayerInfo(0, p.Slot, p.Name, p.Alias ?? p.Name, p.Game, null, null))
            .ToList();

        trackerService.SetProgress(trackerId, new RoomProgressSnapshot
        {
            Players = players.Select(p => new PlayerProgress
            {
                Team = 0,
                Player = p.Slot,
                Alias = p.Alias,
                Game = p.Game,
                ChecksDone = p.Done,
                ChecksTotal = p.Total,
                ClientStatus = p.Status,
                LastActivity = p.Inactive is { } inactive ? now - inactive : null,
                // Roughly the same share of this demo's few locations as of the real total.
                CheckedLocationIds = locationIds[p.Game].Values.Take(p.Done * locationIds[p.Game].Count / p.Total).ToList(),
            }).ToList(),
        });

        connectionManager.RoomSettings = new RoomSettingsSnapshot
        {
            ReleaseMode = "auto",
            CollectMode = "auto",
            RemainingMode = "goal",
            HintCostPercentage = 10,
            LocationCheckPoints = 1,
            HasPassword = false,
        };
    }
}
