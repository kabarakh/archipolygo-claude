using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): <see cref="HintPickerWindow"/>'s
/// real <c>.axaml</c> against a real layout/click pass - see
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md. Constructs the window directly
/// (<c>new HintPickerWindow { DataContext = ... }; window.Show();</c>) rather
/// than through the static <c>Show</c> helper's <c>ShowDialog</c>, same
/// approach <see cref="RemoveConfiguredSlotButtonTests"/> uses for
/// <c>ConnectionEditorWindow</c> - a real simulated click, wired through the
/// actual code-behind/bindings, not a direct command-property call.
/// </summary>
public class HintPickerWindowTests
{
    private static (GroupViewModel viewModel, ServerConnectionGroup group, FakeConnectionManager manager) MakeGroup(params string[] slotNames)
    {
        var manager = new FakeConnectionManager();
        var group = new ServerConnectionGroup { Name = "Test Server" };
        var viewModel = new GroupViewModel(group, manager);

        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return (viewModel, group, manager);
    }

    private static ReceivedItemEntry ReceivedItem(SlotProfile slot, string itemName) => new()
    {
        SlotId = slot.Id,
        ReceivingSlotName = slot.SlotName,
        ItemName = itemName,
        LocationName = "Somewhere",
        SenderName = "SomeSender",
        ItemKind = EventTextSegmentKind.ItemOther,
        SenderKind = EventTextSegmentKind.OtherSlotName,
    };

    /// <summary>One of the picker's row buttons (Content = the row's name), or null if not currently present in the visual tree - distinguishing "not shown" from "shown" is the point of most of these tests, so this deliberately doesn't throw.</summary>
    private static Button? FindRowButtonOrNull(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, name) && b.DataContext is HintPickerRow);

    /// <summary>Real simulated left-click at a control's own center, translated into the window's coordinate space - same technique as RemoveConfiguredSlotButtonTests.</summary>
    private static void Click(Window window, Control control)
    {
        var localCenter = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var pointInWindow = control.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Opening_ShowsItemModeRowsForTheDefaultSlot()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(FindRowButtonOrNull(window, "Fire Rod"));
    }

    [AvaloniaFact]
    public void SwitchingSlotComboBox_ShowsThatSlotsOwnRows_ViaTheRealControl()
    {
        var (viewModel, group, _) = MakeGroup("Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Alice's Item"));
        viewModel.ReceivedItems.Add(ReceivedItem(bob, "Bob's Item"));
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(FindRowButtonOrNull(window, "Alice's Item"));
        Assert.Null(FindRowButtonOrNull(window, "Bob's Item"));

        var comboBox = window.GetVisualDescendants().OfType<ComboBox>().Single();
        comboBox.SelectedItem = bob;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(FindRowButtonOrNull(window, "Alice's Item"));
        Assert.NotNull(FindRowButtonOrNull(window, "Bob's Item"));
    }

    [AvaloniaFact]
    public void ClickingLocationModeRadioButton_SwitchesToLocationRows()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        manager.SetHintableLocations(alice, new[] { new HintableLocation { LocationId = 1, Name = "Chest" } });
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod")); // an item-mode row that must disappear once we switch away
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(FindRowButtonOrNull(window, "Fire Rod"));

        var locationRadio = window.GetVisualDescendants().OfType<RadioButton>().Single(r => Equals(r.Content, "Hint for location"));
        Click(window, locationRadio);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(FindRowButtonOrNull(window, "Fire Rod"));
        Assert.NotNull(FindRowButtonOrNull(window, "Chest"));
    }

    [AvaloniaFact]
    public void SearchTextBox_FiltersVisibleRowButtons()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Ice Rod"));
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var searchBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Search...");
        searchBox.Text = "fire";
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(FindRowButtonOrNull(window, "Fire Rod"));
        Assert.Null(FindRowButtonOrNull(window, "Ice Rod"));
    }

    [AvaloniaFact]
    public void ClickingALocationRow_SendsTheHint_RemovesItImmediately_WindowStaysOpen()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        manager.SetHintableLocations(alice, new[] { new HintableLocation { LocationId = 42, Name = "Chest" } });
        viewModel.HintPicker.Mode = HintTargetMode.Location;
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var chestButton = FindRowButtonOrNull(window, "Chest");
        Assert.NotNull(chestButton);
        Click(window, chestButton!);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed); // dev feedback: sending a hint must not close the picker
        Assert.Null(FindRowButtonOrNull(window, "Chest")); // gone right away - no re-open needed
        Assert.Contains((alice.Id, 42L), manager.SentLocationHints);
    }

    [AvaloniaFact]
    public void ClickingAnItemRow_SendsTheHint_ButLeavesTheRowInPlace()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rowButton = FindRowButtonOrNull(window, "Fire Rod");
        Assert.NotNull(rowButton);
        Click(window, rowButton!);
        Dispatcher.UIThread.RunJobs();

        // A deduped item name might still have other unhinted copies this
        // app can't tell apart from the one just hinted - see
        // HintPickerViewModel.SendAsync.
        Assert.NotNull(FindRowButtonOrNull(window, "Fire Rod"));
        Assert.Contains((alice.Id, "Fire Rod"), manager.SentItemHints);
    }

    [AvaloniaFact]
    public void ClickingCloseButton_ClosesTheWindow()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var closeButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close"));
        Click(window, closeButton);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void NothingAvailable_ShowsTheEmptyStateText_NotTheRowList()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        // No ReceivedItems/Hints seeded - Item mode's default candidate pool is empty.
        viewModel.HintPicker.OnOpened();

        var window = new HintPickerWindow { DataContext = viewModel.HintPicker };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var emptyText = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => Equals(t.Text, "Nothing available for this slot/mode."));
        Assert.NotNull(emptyText);
        Assert.True(emptyText!.IsEffectivelyVisible);
    }
}
