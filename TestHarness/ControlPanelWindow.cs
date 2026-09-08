using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Layout;
using TestHarness.AddSlotPickerPrototype;

namespace TestHarness;

/// <summary>
/// Plain code-built (no .axaml) auxiliary window - see
/// .claude/skills/ui-feature-prototyp/SKILL.md, TestHarness's own purpose.
/// Most buttons inject synthetic data directly into <see cref="GroupViewModel"/>'s
/// public collections, bypassing IConnectionManager entirely, to drive the real
/// Events/Hints/Items panels without a network connection. The "Hint routing"
/// section is the exception - it goes through <see cref="FakeConnectionManager"/>'s
/// fake hint room, because that's the part that actually models the
/// leader/non-leader subscription behavior fixed in ConnectionManager.cs.
/// </summary>
public sealed class ControlPanelWindow : Window
{
    private readonly GroupViewModel _group;
    private readonly SlotProfile _slot;
    private readonly SlotProfile _siblingSlot;
    private readonly FakeConnectionManager _connectionManager;
    private readonly TextBlock _leaderStatusText;
    private readonly TextBlock _addSlotPickerResultText;
    private int _counter;
    private int _hintCounter;

    public ControlPanelWindow(GroupViewModel group, SlotProfile slot, SlotProfile siblingSlot, FakeConnectionManager connectionManager)
    {
        _group = group;
        _slot = slot;
        _siblingSlot = siblingSlot;
        _connectionManager = connectionManager;

        Title = "Test Control Panel";
        Width = 260;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new Avalonia.PixelPoint(20, 20);

        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Avalonia.Thickness(10) };

        panel.Children.Add(new TextBlock { Text = "Chat / connect", FontWeight = Avalonia.Media.FontWeight.Bold });
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
            Text = "Add-Slot Redesign (Prototyp)",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Suchfilter + Checkbox-Mehrfachauswahl statt ComboBox + \"Add to list\" - siehe Umsetzungsplan.md.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        panel.Children.Add(Btn("Open Add-Slot picker (prototype)", () => _ = OpenAddSlotPickerPrototypeAsync()));
        _addSlotPickerResultText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 };
        panel.Children.Add(_addSlotPickerResultText);

        Content = new ScrollViewer { Content = panel };
    }

    private static Button Btn(string text, Action action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddChat()
    {
        _counter++;
        _group.Events.Add(new EventEntry
        {
            Type = EventType.Chat,
            Text = $"SomePlayer: hello #{_counter}",
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

    /// <summary>
    /// Finds whichever of the two demo slots is currently NOT the leader and
    /// simulates it running "!hint_location" for a hint whose receiving
    /// player is an external, unconfigured room player - i.e. neither side of
    /// the hint is the leader. Before the ConnectionManager.cs fix, this
    /// hint would never have reached the leader's session at all (see
    /// FakeConnectionManager.SimulateHintLocation); after the fix, it should
    /// show up in this slot's Hints/Events even though it never itself
    /// connects.
    /// </summary>
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

    /// <summary>
    /// Opens the redesigned "Add slot" picker prototype (see
    /// AddSlotPickerPrototype/) with a synthetic room roster - one name
    /// deliberately hints at needing its own password, to give the
    /// per-row 🔒-override something realistic to try. Not wired into
    /// MainWindowViewModel.AddSlotsToGroup - this is purely a click-through
    /// design prototype (see .claude/skills/ui-feature-prototyp); the
    /// result is only echoed back into this panel's text block, nothing is
    /// actually added to the demo group.
    /// </summary>
    private async Task OpenAddSlotPickerPrototypeAsync()
    {
        var syntheticPlayers = new List<PlayerChoice>
        {
            new() { SlotName = "Alice", DisplayText = "Alice" },
            new() { SlotName = "Bob", DisplayText = "Bob (bobby99)" },
            new() { SlotName = "Charlie", DisplayText = "Charlie" },
            new() { SlotName = "Dana", DisplayText = "Dana" },
            new() { SlotName = "Eve", DisplayText = "Eve (custom-hosted, needs own password)" },
            new() { SlotName = "Frank", DisplayText = "Frank" },
            new() { SlotName = "Grace", DisplayText = "Grace" },
            new() { SlotName = "Heidi", DisplayText = "Heidi" },
            new() { SlotName = "Ivan", DisplayText = "Ivan" },
            new() { SlotName = "Judy", DisplayText = "Judy" },
        };

        var viewModel = new AddSlotPickerPrototypeViewModel(syntheticPlayers);
        var window = new AddSlotPickerPrototypeWindow
        {
            DataContext = viewModel,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new Avalonia.PixelPoint(760, 20)
        };

        var result = await window.ShowDialog<IReadOnlyList<StagedSlot>?>(this);

        _addSlotPickerResultText.Text = result is null
            ? "Cancelled."
            : result.Count == 0
                ? "Confirmed with 0 slots (shouldn't happen - Apply is disabled until something is checked)."
                : "Would add: " + string.Join(", ", result.Select(s => s.Password is null ? s.SlotName : $"{s.SlotName} (password set)"));
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
