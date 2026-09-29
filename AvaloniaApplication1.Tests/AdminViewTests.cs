using System.Linq;
using Archipolygo.Models;
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
/// Kategorie C (Test-Umsetzungsplan.md): the Admin view's wiring in the real
/// <c>.axaml</c> (Admin-Funktionen.md in the feature-plan archive) - the
/// "Admin" button next to Hints/Items only for a server marked "I'm the
/// admin", and the Admin view replacing the Hints/Items filter bar and lists.
/// Plus the "I'm the admin" checkbox's round trip through the editor.
/// </summary>
public class AdminViewTests
{
    private static (MainWindow window, GroupViewModel group) SetUpTab(bool isAdmin)
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false, isAdmin: isAdmin);
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 1400, Height = 700 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
        return (window, mainWindowViewModel.Groups[0]);
    }

    private static Button? AdminButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Admin"));

    [AvaloniaFact]
    public void AdminButton_OnlyForServersMarkedAsAdmin()
    {
        var (window, group) = SetUpTab(isAdmin: false);
        Assert.False(AdminButton(window)?.IsVisible ?? false);

        group.Group.IsAdmin = true;
        Dispatcher.UIThread.RunJobs();

        var button = AdminButton(window);
        Assert.NotNull(button);
        Assert.True(button!.IsVisible);
    }

    [AvaloniaFact]
    public void AdminPanel_ReplacesTheHintsFilterBarAndList()
    {
        var (window, group) = SetUpTab(isAdmin: true);
        var adminPanel = window.GetVisualDescendants().OfType<AdminPanelView>().Single();
        var hintsList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "HintsListBox");
        Assert.False(adminPanel.IsEffectivelyVisible);
        Assert.True(hintsList.IsEffectivelyVisible);

        group.SelectedRightPanel = RightPanelView.Admin;
        Dispatcher.UIThread.RunJobs();

        Assert.True(adminPanel.IsEffectivelyVisible);
        Assert.False(hintsList.IsEffectivelyVisible);
        Assert.Same(group.Admin, adminPanel.DataContext);
    }

    [AvaloniaFact]
    public void ConnectionEditor_ShowsIsAdminCheckbox_ForNewAndEditButNotAddSlot()
    {
        var group = new ServerConnectionGroup { Name = "S", Host = "h", Port = 1, IsAdmin = true };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });

        Assert.True(ConnectionEditorViewModel.ForNewGroup().ShowIsAdmin);
        var edit = ConnectionEditorViewModel.ForEditGroup(group);
        Assert.True(edit.ShowIsAdmin);
        Assert.True(edit.IsAdmin);
        Assert.False(ConnectionEditorViewModel.ForAddSlot(group, System.Array.Empty<PlayerChoice>()).ShowIsAdmin);

        edit.IsAdmin = false;
        Assert.True(edit.TryBuildResult(out var result));
        Assert.False(result.IsAdmin);
    }
}
