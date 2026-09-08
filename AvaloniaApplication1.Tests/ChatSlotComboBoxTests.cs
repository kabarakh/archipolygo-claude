using System.Linq;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): locks in the contract
/// <see cref="MainWindow.OnChatSlotComboBoxLoaded"/> exists for - a tab that
/// was never the selected one since app start only builds its visual tree
/// (this ComboBox included) the first time the user actually clicks it, long
/// after <see cref="GroupViewModel.SelectedChatSlot"/> was already set
/// correctly; once activated, the ComboBox must show that value.
///
/// Honest caveat, checked by temporarily deleting the <c>Loaded=</c> wiring
/// and rerunning this test: it still passes without the fix. The original
/// bug was a real-window ComboBox SelectedItem-vs-ItemsSource binding race
/// that <see cref="Dispatcher.RunJobs"/>'s single drain-to-fixed-point pass
/// apparently doesn't reproduce - Avalonia.Headless's simplified dispatcher
/// isn't a faithful enough stand-in for that specific timing. This test is
/// kept anyway because it still pins down the correct end state (and would
/// catch a *different* future regression that broke it outright), but it is
/// not proof the Loaded handler is still needed - only running the real app
/// and actually clicking a never-before-selected tab by hand can currently
/// show that (the manual screenshot/click skill this used to fall back on
/// was retired once Kategorie A-C reached this coverage - see
/// Test-Umsetzungsplan.md).
/// </summary>
public class ChatSlotComboBoxTests
{
    [AvaloniaFact]
    public void OnFirstTabActivation_ChatSlotComboBox_ResolvesSelectedItemCorrectly()
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager);

        // Two servers, each auto-connecting its own single slot as leader
        // right away (see MainWindowViewModel.AddNewGroup) - deterministic
        // and synchronous here since FakeConnectionManager.SwitchLeaderAsync
        // never actually awaits anything.
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var group1 = mainWindowViewModel.Groups[0];
        var group2 = mainWindowViewModel.Groups[1];

        // AddNewGroup selects whichever group it just added (group2) - force
        // group1 to be the one selected when the window first shows, so
        // group2's tab is never the active one at "app start", exactly the
        // scenario OnChatSlotComboBoxLoaded exists for.
        mainWindowViewModel.SelectedGroup = group1;

        var window = new MainWindow { DataContext = mainWindowViewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Sanity check: group2's ComboBox genuinely doesn't exist yet - if this
        // ever fails, the test below would trivially pass for the wrong reason
        // (nothing lazy about it anymore).
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<ComboBox>(),
            c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group2));

        // Simulate the user clicking the second tab for the first time.
        mainWindowViewModel.SelectedGroup = group2;
        Dispatcher.UIThread.RunJobs();

        var group2ComboBox = window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(c => c.Name == "ChatSlotComboBox" && ReferenceEquals(c.DataContext, group2));
        Assert.NotNull(group2ComboBox);
        Assert.Same(group2.SelectedChatSlot, group2ComboBox!.SelectedItem);
        Assert.Equal("Bob", ((Archipolygo.Models.SlotProfile)group2ComboBox.SelectedItem!).SlotName);
    }
}
