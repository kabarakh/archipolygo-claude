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
/// Pins the end state OnChatSlotComboBoxLoaded exists for (lazily materialized tab's ComboBox showing SelectedChatSlot).
/// Caveat: passes even without the Loaded fix - the headless dispatcher doesn't reproduce the real-window binding race.
/// </summary>
public class ChatSlotComboBoxTests
{
    [AvaloniaFact]
    public void OnFirstTabActivation_ChatSlotComboBox_ResolvesSelectedItemCorrectly()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());

        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];

        // group2's tab must never be the active one at startup.
        mainWindowViewModel.SelectedGroup = group1;

        var window = new MainWindow { DataContext = mainWindowViewModel };
        window.Show();

        // Tab content only materializes once the TabControl is visible; the app starts on the Dashboard.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        // Sanity check: otherwise the test would pass for the wrong reason.
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<ComboBox>(),
            c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group2));

        // First click on the second tab.
        mainWindowViewModel.SelectedGroup = group2;
        Dispatcher.UIThread.RunJobs();

        var group2ComboBox = window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group2));
        Assert.NotNull(group2ComboBox);
        Assert.Same(group2.SelectedChatSlot, group2ComboBox!.SelectedItem);
        Assert.Equal("Bob", ((Archipolygo.Models.SlotProfile)group2ComboBox.SelectedItem!).SlotName);
    }
}
