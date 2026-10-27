using System;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TestHarness;

/// <summary>
/// Code-built auxiliary window that injects synthetic data straight into GroupViewModel's collections; only the
/// "Hint routing" section goes through FakeConnectionManager's fake hint room.
/// </summary>
public sealed class ControlPanelWindow : Window
{
    private readonly GroupViewModel _group;
    private readonly SlotProfile _slot;
    private readonly SlotProfile _siblingSlot;
    private readonly FakeConnectionManager _connectionManager;
    private readonly TextBlock _leaderStatusText;
    private readonly IAttentionTracker _attentionTracker;
    private int _counter;
    private int _hintCounter;

    public ControlPanelWindow(GroupViewModel group, SlotProfile slot, SlotProfile siblingSlot, FakeConnectionManager connectionManager,
                              IAttentionTracker attentionTracker)
    {
        _attentionTracker = attentionTracker;
        _group = group;
        _slot = slot;
        _siblingSlot = siblingSlot;
        _connectionManager = connectionManager;

        Title = "Test Control Panel";
        Width = 260;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new Avalonia.PixelPoint(20, 20);

        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Avalonia.Thickness(10) };

        AddItemLinksSection(panel);
        AddAdminSection(panel, group, connectionManager);

        panel.Children.Add(new TextBlock { Text = "Notifications (Benachrichtigungen.md)", FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "Buttons below report to the real AttentionTracker for TestServer. Clicking here makes the main window inactive, so it can blink. Switch tabs / Dashboard in the main window to see when the count resets. Settings and \"Mute notifications\" (Edit server) are the real dialogs in the main window.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(Btn("Progression item arrives", () => { AddItem(ItemFlags.Advancement, "Progression Sword"); Report(AttentionCategory.ProgressionItem); }));
        panel.Children.Add(Btn("Filler item arrives", () => { AddItem(ItemFlags.None, "Filler Junk"); Report(AttentionCategory.OtherItem); }));
        panel.Children.Add(Btn("Hint for my slot arrives", () => { AddHint(ItemFlags.Advancement, "Progression Key"); Report(AttentionCategory.OwnHint); }));
        panel.Children.Add(Btn("DeathLink arrives", () => Report(AttentionCategory.DeathLink)));
        panel.Children.Add(Btn("Chat mentioning TestSlot", () => { AddChat($"SomePlayer: TestSlot, do you have the hookshot?"); Report(AttentionCategory.ChatMention); }));
        panel.Children.Add(Btn("Other chat message", () => { AddChat(); Report(AttentionCategory.Chat); }));
        panel.Children.Add(Btn("Progression item in 3 s (switch away!)", () =>
            Avalonia.Threading.DispatcherTimer.RunOnce(() => { AddItem(ItemFlags.Advancement, "Delayed Sword"); Report(AttentionCategory.ProgressionItem); }, TimeSpan.FromSeconds(3))));

        panel.Children.Add(new TextBlock { Text = "Chat / connect", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
        panel.Children.Add(Btn("Add chat line", AddChat));

        panel.Children.Add(new TextBlock { Text = "Items (event + received-items list)", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
        panel.Children.Add(Btn("Add Progression item", () => AddItem(ItemFlags.Advancement, "Progression Sword")));
        panel.Children.Add(Btn("Add Useful item", () => AddItem(ItemFlags.NeverExclude, "Useful Shield")));
        panel.Children.Add(Btn("Add Filler item", () => AddItem(ItemFlags.None, "Filler Junk")));
        panel.Children.Add(Btn("Add Trap item", () => AddItem(ItemFlags.Trap, "Trap Bomb")));

        panel.Children.Add(new TextBlock { Text = "Hints (event + hints panel)", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
        panel.Children.Add(Btn("Add Progression hint", () => AddHint(ItemFlags.Advancement, "Progression Key")));
        panel.Children.Add(Btn("Add Useful hint", () => AddHint(ItemFlags.NeverExclude, "Useful Potion")));
        panel.Children.Add(Btn("Add Filler hint", () => AddHint(ItemFlags.None, "Filler Coin")));
        panel.Children.Add(Btn("Add Trap hint", () => AddHint(ItemFlags.Trap, "Trap Curse")));

        panel.Children.Add(new TextBlock { Text = "Bulk", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
        panel.Children.Add(Btn("Add one of everything", AddOneOfEverything));

        panel.Children.Add(new TextBlock
        {
            Text = "Hint routing (leader vs. non-leader)",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 10, 0, 0),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Reproduces the 0.1.0 bug: a hint for a location owned by a non-leader configured slot never reached the event log.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _leaderStatusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(_leaderStatusText);
        panel.Children.Add(Btn($"Switch leader to {_slot.SlotName}", () => SwitchLeader(_slot)));
        panel.Children.Add(Btn($"Switch leader to {_siblingSlot.SlotName}", () => SwitchLeader(_siblingSlot)));
        panel.Children.Add(Btn("Simulate !hint_location as non-leader", SimulateHintAsNonLeader));
        RefreshLeaderStatus();

        panel.Children.Add(new TextBlock
        {
            Text = "Tab-Reordering (Drag & Drop)",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Nicht separat nachgebaut - das ist bereits das echte MainWindow/DashboardView " +
                   "mit vier Demo-Servern. Zum Ausprobieren: im Dashboard (Startansicht) eine " +
                   "Overview-Zeile per Drag auf eine andere ziehen, oder auf \"Tab View\" wechseln " +
                   "und dasselbe mit den Tab-Headern oben tun. Siehe Feature-Plaene/Tab-Reihenfolge.md.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        Content = new ScrollViewer { Content = panel };
    }

    // Slot numbers match SeedAdminDemo's roster in TestHarnessApp.
    private void AddItemLinksSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Item links", FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "TestServer → \"Item links\" next to Hints/Items. Hover a group name in Events or Hints for its members.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(Btn("Room has 3 item links", SeedItemLinks));
        panel.Children.Add(Btn("Room has no item links", () => _group.ApplyItemLinkGroups([])));
        panel.Children.Add(Btn("Item + hint for a link group", AddItemLinkEvents));
        panel.Children.Add(Btn("All of \"Samus Link\" goal", () => _group.ApplyGoals(null, [3, 6, 11])));
    }

    private void SeedItemLinks()
    {
        RoomPlayerRef P(int slot, string name, string? alias = null) => new(slot, name, SlotProfile.FormatDisplayName(name, alias));
        _group.ApplyItemLinkGroups(
        [
            new(P(9, "Sword Link"), "A Link to the Past", [P(1, _slot.SlotName), P(4, "Player One"), P(8, "Nightowl")]),
            new(P(10, "Bug Link"), "Hollow Knight", [P(2, _siblingSlot.SlotName), P(5, "Kestrel"), P(7, "Latecomer")]),
            new(P(11, "Samus Link"), "Super Metroid", [P(3, "Mira", "Mira the Brave"), P(6, "Done Dan")]),
        ]);
    }

    private void AddItemLinkEvents()
    {
        _counter++;
        var group = _group.GetRoomSlotStatus(9);
        var finder = _group.GetRoomSlotStatus(3);
        _group.Events.Add(new EventEntry
        {
            Type = EventType.ItemReceived,
            Text = $"Mira sent Hookshot to Sword Link (Varia Suit #{_counter})",
            Segments =
            [
                new("Mira (Mira the Brave)", EventTextSegmentKind.OtherSlotName, finder),
                new(" sent ", EventTextSegmentKind.PlainText),
                new("Hookshot", EventTextSegmentKind.ItemProgression),
                new(" to ", EventTextSegmentKind.PlainText),
                new("Sword Link", EventTextSegmentKind.OtherSlotName, group),
                new($" (Varia Suit #{_counter})", EventTextSegmentKind.PlainText),
            ],
            SlotId = _slot.Id,
        });

        _group.Hints.Add(new HintEntry
        {
            Key = Guid.NewGuid().ToString(),
            SlotId = _slot.Id,
            ReceivingPlayer = 9,
            FindingPlayer = 3,
            ReceivingPlayerName = "Sword Link",
            FindingPlayerName = "Mira (Mira the Brave)",
            ReceivingSlot = group,
            FindingSlot = finder,
            ItemName = "Moon Pearl",
            LocationName = $"Speed Booster #{_counter}",
            ItemFlags = ItemFlags.Advancement,
            ItemKind = EventTextSegmentKind.ItemProgression,
            ReceivingPlayerKind = EventTextSegmentKind.OtherSlotName,
            FindingPlayerKind = EventTextSegmentKind.OtherSlotName,
            Found = false,
        });
    }

    private static void AddAdminSection(StackPanel panel, GroupViewModel group, FakeConnectionManager connectionManager)
    {
        panel.Children.Add(new TextBlock { Text = "Admin (Admin-Funktionen.md)", FontWeight = Avalonia.Media.FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "TestServer tab → \"Admin\" next to Hints/Items. Fake admin password: admin. The \"I'm the admin\" checkbox is also in Edit server / Add server.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var isAdmin = new CheckBox { Content = "TestServer: I'm the admin", IsChecked = group.Group.IsAdmin };
        isAdmin.IsCheckedChanged += (_, _) => group.Group.IsAdmin = isAdmin.IsChecked == true;
        group.Group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServerConnectionGroup.IsAdmin))
            {
                isAdmin.IsChecked = group.Group.IsAdmin;
            }
        };
        panel.Children.Add(isAdmin);

        var hasPassword = new CheckBox { Content = "Server has an admin password", IsChecked = true };
        hasPassword.IsCheckedChanged += (_, _) => connectionManager.AdminServerPassword = hasPassword.IsChecked == true ? "admin" : null;
        panel.Children.Add(hasPassword);

        var trackerId = group.Group.TrackerId;
        var hasTracker = new CheckBox { Content = "Tracker id set", IsChecked = trackerId is not null };
        hasTracker.IsCheckedChanged += (_, _) => group.Group.TrackerId = hasTracker.IsChecked == true ? trackerId : null;
        panel.Children.Add(hasTracker);

        panel.Children.Add(Btn("Another client logs in as admin", () => connectionManager.SimulateOtherClientTakesOverAdmin(group)));
        panel.Children.Add(Btn("Simulate reconnect", () =>
        {
            var leader = group.Group.Slots.FirstOrDefault(s => s.Id == group.LeaderSlotId);
            if (leader is not null)
            {
                _ = connectionManager.SwitchLeaderAsync(group, leader);
            }
        }));
    }

    private static Button Btn(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        return button;
    }

    private void Report(AttentionCategory category) => _attentionTracker.Report(_group, category);

    private void AddChat() => AddChat(null);

    private void AddChat(string? text)
    {
        _counter++;
        _group.Events.Add(new EventEntry
        {
            Type = EventType.Chat,
            Text = text ?? $"SomePlayer: hello #{_counter}",
            SlotId = null,
            ConcernsOwnSlot = false,
        });
    }

    private void AddItem(ItemFlags flags, string itemName)
    {
        _counter++;
        var locationName = $"Some Location #{_counter}";

        _group.Events.Add(new EventEntry
        {
            Type = EventType.ItemReceived,
            Text = $"Received {itemName} ({locationName})",
            Segments = EventSegmentBuilder.BuildItemReceivedSegments(itemName, flags, locationName),
            SlotId = _slot.Id,
        });

        _group.ReceivedItems.Add(new ReceivedItemEntry
        {
            SlotId = _slot.Id,
            ReceivingSlotName = _slot.SlotName,
            ItemName = itemName,
            LocationName = locationName,
            SenderName = "SomeOtherPlayer",
            ItemKind = EventSegmentBuilder.ClassifyItemFlags(flags),
            SenderKind = EventTextSegmentKind.OtherSlotName,
        });
    }

    private void AddHint(ItemFlags flags, string itemName)
    {
        _counter++;
        var locationName = $"Hinted Location #{_counter}";
        var itemKind = EventSegmentBuilder.ClassifyItemFlags(flags);

        _group.Events.Add(new EventEntry
        {
            Type = EventType.HintReceived,
            Text = $"Hint: {itemName} (SomeFinder -> {_slot.SlotName}, {locationName})",
            Segments = EventSegmentBuilder.BuildHintReceivedSegments(
                itemName, flags,
                "SomeFinder", EventTextSegmentKind.OtherSlotName,
                _slot.SlotName, EventTextSegmentKind.OwnSlotName,
                locationName),
            SlotId = _slot.Id,
        });

        _group.Hints.Add(new HintEntry
        {
            Key = Guid.NewGuid().ToString(),
            SlotId = _slot.Id,
            ReceivingPlayer = 1,
            FindingPlayer = 2,
            ReceivingPlayerName = _slot.SlotName,
            FindingPlayerName = "SomeFinder",
            ItemName = itemName,
            LocationName = locationName,
            ItemFlags = flags,
            ItemKind = itemKind,
            ReceivingPlayerKind = EventTextSegmentKind.OwnSlotName,
            FindingPlayerKind = EventTextSegmentKind.OtherSlotName,
            Found = false,
        });
    }

    private void SwitchLeader(SlotProfile targetSlot)
    {
        _ = _connectionManager.SwitchLeaderAsync(_group, targetSlot);
        RefreshLeaderStatus();
    }

    private void RefreshLeaderStatus()
    {
        var leaderName = _group.Group.Slots.FirstOrDefault(s => s.Id == _group.LeaderSlotId)?.SlotName ?? "(none)";
        _leaderStatusText.Text = $"Current leader: {leaderName}";
    }

    /// <summary>Neither side of the hint is the leader; with the fix it still reaches this slot's Hints/Events.</summary>
    private void SimulateHintAsNonLeader()
    {
        var nonLeader = _group.Group.Slots.FirstOrDefault(s => s.Id != _group.LeaderSlotId) ?? _siblingSlot;

        _hintCounter++;
        _connectionManager.SimulateHintLocation(
            _group,
            nonLeader,
            receiverName: "ExternalPlayer",
            itemName: $"External Item #{_hintCounter}",
            locationName: $"{nonLeader.SlotName}'s Location #{_hintCounter}",
            flags: ItemFlags.Advancement);
    }

    private void AddOneOfEverything()
    {
        AddChat();
        AddItem(ItemFlags.Advancement, "Progression Sword");
        AddItem(ItemFlags.NeverExclude, "Useful Shield");
        AddItem(ItemFlags.None, "Filler Junk");
        AddItem(ItemFlags.Trap, "Trap Bomb");
        AddHint(ItemFlags.Advancement, "Progression Key");
        AddHint(ItemFlags.NeverExclude, "Useful Potion");
        AddHint(ItemFlags.None, "Filler Coin");
        AddHint(ItemFlags.Trap, "Trap Curse");
    }
}
