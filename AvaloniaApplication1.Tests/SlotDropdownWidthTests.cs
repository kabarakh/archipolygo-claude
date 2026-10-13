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

/// <summary>A long "SlotName (Alias)" must not resize the dropdowns (needs a real Width, not just MinWidth).</summary>
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

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
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

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
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

    /// <summary>Virtualized popup items are measured on demand, so without the item template's MaxWidth a long name balloons the popup (~868px).</summary>
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

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
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
