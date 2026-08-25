using System;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TestHarness;

/// <summary>
/// Plain code-built (no .axaml) auxiliary window - see .claude/skills/app-testen.
/// Each button injects synthetic data directly into <see cref="GroupViewModel"/>'s
/// public collections, bypassing IConnectionManager entirely, to drive the real
/// Events/Hints/Items panels without a network connection.
/// </summary>
public sealed class ControlPanelWindow : Window
{
    private readonly GroupViewModel _group;
    private readonly SlotProfile _slot;
    private int _counter;

    public ControlPanelWindow(GroupViewModel group, SlotProfile slot)
    {
        _group = group;
        _slot = slot;

        Title = "Test Control Panel";
        Width = 260;
        Height = 520;
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
