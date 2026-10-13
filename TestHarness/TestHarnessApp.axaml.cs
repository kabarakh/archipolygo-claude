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

            // Real attention pipeline so the control panel's notification buttons really blink/badge this window.
            var groupWindowLocator = new Archipolygo.Services.GroupWindowLocator();
            var attentionTracker = new Archipolygo.Services.AttentionTracker(
                new Archipolygo.Services.WindowAttentionService(groupWindowLocator), persistenceService);
            var trackerService = new FakeMultiworldTrackerService();
            var mainWindowViewModel = new MainWindowViewModel(persistenceService, connectionManager, trackerService,
                attentionTracker: attentionTracker);

            // The second slot lets the control panel demonstrate leader-vs-non-leader hint routing.
            mainWindowViewModel.AddNewGroup("TestServer", "localhost", 38281, string.Empty, "TestSlot", autoConnect: true);
            var groupViewModel = mainWindowViewModel.Groups[0];
            var slot = groupViewModel.Group.Slots[0];

            mainWindowViewModel.AddSlotsToGroup(groupViewModel, new List<StagedSlot>
            {
                new() { SlotName = "SiblingSlot", DisplayText = "SiblingSlot" }
            });
            var siblingSlot = groupViewModel.Group.Slots[1];

            // The leader connected before this sibling existed; re-run it so the sibling's hint key gets tracked.
            _ = connectionManager.SwitchLeaderAsync(groupViewModel, slot);

            connectionManager.SetHintableLocations(slot, new List<Archipolygo.Models.HintableLocation>
            {
                new() { LocationId = 1, Name = "Cave Entrance - Chest" },
                new() { LocationId = 2, Name = "Forest Clearing - Tree Stump" },
                new() { LocationId = 3, Name = "Mountain Pass - Summit Chest" },
                new() { LocationId = 4, Name = "Old Library - Basement" },
                new() { LocationId = 5, Name = "Village Square - Well" },
            });

            // More servers purely so there are tabs/rows to drag; TestServer stays first since the control panel targets it.
            mainWindowViewModel.AddNewGroup("AlphaServer", "alpha.example", 38281, string.Empty, "AlphaSlot", autoConnect: false);
            mainWindowViewModel.AddNewGroup("BravoServer", "bravo.example", 38281, string.Empty, "BravoSlot", autoConnect: false);
            mainWindowViewModel.AddNewGroup("CharlieServer", "charlie.example", 38281, string.Empty, "CharlieSlot", autoConnect: false);

            // AddNewGroup selects the last added group; reselect TestServer.
            mainWindowViewModel.SelectedGroup = groupViewModel;

            SeedAdminDemo(groupViewModel, connectionManager, trackerService);

            // Positioned so it never overlaps ControlPanelWindow at (20,20), 260 wide.
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual,
                Position = new Avalonia.PixelPoint(300, 20)
            };

            // Same dialog wiring as App.axaml.cs so the harness behaves like the real app minus the network.
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

    /// <summary>Marks TestServer as admin with fake roster/tracker data; the fake admin password is "admin".</summary>
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
