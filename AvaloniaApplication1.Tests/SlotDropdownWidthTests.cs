using System.Linq;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the "Chat as"/slot-filter dropdowns'
/// own closed-state box must never resize depending on which slot happens to
/// be selected - previously only <c>MinWidth</c> was set, so a long combined
/// "SlotName (Alias)" (see <see cref="Archipolygo.Models.SlotProfile.DisplayName"/>)
/// visibly grew the box and shifted whatever sits next to it (the message
/// box/Send button for "Chat as", the search box for the slot filters).
/// Fixed via a real <c>Width</c> (not just <c>MinWidth</c>) plus
/// <c>TextTrimming="CharacterEllipsis"</c> - verified here against real
/// <c>Bounds</c> after a real layout pass, not just the XAML attribute.
/// </summary>
public class SlotDropdownWidthTests
{
    [AvaloniaFact]
    public void ChatAsComboBox_WidthStaysConstant_RegardlessOfSelectedSlotsNameLength()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Bo", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var longNamedSlot = new Archipolygo.Models.SlotProfile
        {
            GroupId = group.Group.Id,
            SlotName = "AVeryLongConfiguredSlotName",
            Alias = "AndAnEvenLongerCustomAliasOnTopOfThat",
        };
        group.AddSlotsToGroup(new[] { longNamedSlot });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once the TabControl is actually
        // visible (see MainWindow.axaml's lazy ContentTemplate gotcha,
        // documented in CLAUDE.md) - the Dashboard is the default view on
        // startup, so switch away from it first.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var comboBox = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group));

        group.SelectedChatSlot = group.Group.Slots.Single(s => s.SlotName == "Bo");
        Dispatcher.UIThread.RunJobs();
        var widthWithShortName = comboBox.Bounds.Width;

        group.SelectedChatSlot = longNamedSlot;
        Dispatcher.UIThread.RunJobs();
        var widthWithLongCombinedName = comboBox.Bounds.Width;

        Assert.True(widthWithShortName > 0, "sanity check: the box should have measured to a real width at all");
        Assert.Equal(widthWithShortName, widthWithLongCombinedName);
        Assert.Equal(160, widthWithLongCombinedName); // the fixed Width in MainWindow.axaml
    }

    [AvaloniaFact]
    public void EventsSlotFilterComboBox_WidthStaysConstant_RegardlessOfSelectedSlotsNameLength()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Bo", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var longNamedSlot = new Archipolygo.Models.SlotProfile
        {
            GroupId = group.Group.Id,
            SlotName = "AVeryLongConfiguredSlotName",
            Alias = "AndAnEvenLongerCustomAliasOnTopOfThat",
        };
        group.AddSlotsToGroup(new[] { longNamedSlot });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once the TabControl is actually
        // visible (see MainWindow.axaml's lazy ContentTemplate gotcha,
        // documented in CLAUDE.md) - the Dashboard is the default view on
        // startup, so switch away from it first.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var comboBox = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "EventsSlotFilterComboBox" && ReferenceEquals(c.DataContext, group));

        group.SelectedEventsSlotFilter = null; // "All slots"
        Dispatcher.UIThread.RunJobs();
        var widthShowingAllSlots = comboBox.Bounds.Width;

        group.SelectedEventsSlotFilter = longNamedSlot;
        Dispatcher.UIThread.RunJobs();
        var widthShowingLongCombinedName = comboBox.Bounds.Width;

        Assert.True(widthShowingAllSlots > 0, "sanity check: the box should have measured to a real width at all");
        Assert.Equal(widthShowingAllSlots, widthShowingLongCombinedName);
        Assert.Equal(130, widthShowingLongCombinedName); // the fixed Width in MainWindow.axaml
    }

    /// <summary>
    /// The second half of the same bug: even with the closed box's own Width
    /// pinned (see the two tests above), the OPEN dropdown's popup was still
    /// visibly growing wider once a long item scrolled into view and got
    /// measured for the first time - Avalonia only realizes/measures
    /// virtualized popup items on demand, so an Auto-sized item's own
    /// measured width (nothing capped it before the item template's
    /// TextBlock got a MaxWidth) could still balloon the whole popup well
    /// past the closed box's width. Confirmed empirically: removing the
    /// ItemTemplate's MaxWidth made a long combined name's popup item
    /// measure to ~868px in this exact scenario (rather than a small,
    /// per-item difference), instead of being capped/trimmed.
    /// </summary>
    [AvaloniaFact]
    public void ChatAsComboBox_OpenPopupItem_WidthIsCapped_NotAllowedToBalloon()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Bo", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var longNamedSlot = new Archipolygo.Models.SlotProfile
        {
            GroupId = group.Group.Id,
            SlotName = "AVeryLongConfiguredSlotName",
            Alias = "AndAnEvenLongerCustomAliasOnTopOfThat",
        };
        group.AddSlotsToGroup(new[] { longNamedSlot });

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once the TabControl is actually
        // visible (see MainWindow.axaml's lazy ContentTemplate gotcha,
        // documented in CLAUDE.md) - the Dashboard is the default view on
        // startup, so switch away from it first.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var comboBox = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group));

        comboBox.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        var popupItemText = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => ReferenceEquals(t.DataContext, longNamedSlot));

        Assert.True(popupItemText.Bounds.Width <= 140.5,
            $"expected the long combined name's popup item to be capped at the ItemTemplate's MaxWidth (140), " +
            $"got {popupItemText.Bounds.Width} - the popup can balloon well past the closed box's own Width otherwise.");
    }
}
