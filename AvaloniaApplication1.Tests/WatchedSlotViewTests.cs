using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>The watched slot in the real views, plus the editor and Save path that set it.</summary>
public class WatchedSlotViewTests
{
    private static (MainWindow Window, MainWindowViewModel ViewModel, GroupViewModel Group, FakeConnectionManager Connections) ShowTab(string? watchedSlotName)
    {
        var connectionManager = new FakeConnectionManager();
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 12345, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        group.Group.WatchedSlotName = watchedSlotName;

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 1100, Height = 650 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
        return (window, mainWindowViewModel, group, connectionManager);
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(control => control.Name == name);

    private static WatchedItemRow Row(string itemName) =>
        new() { ItemName = itemName, SenderName = "Mira", LocationName = "Crystal Peak", IsNew = true };

    [AvaloniaFact]
    public void Strip_IsShownOnlyWithAWatchedSlot()
    {
        var (window, _, group, _) = ShowTab(watchedSlotName: null);
        Assert.False(Named<Border>(window, "WatchedItemsStrip").IsVisible);

        group.Group.WatchedSlotName = "malandor";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<Border>(window, "WatchedItemsStrip").IsVisible);
    }

    [AvaloniaFact]
    public void CollapsedStrip_ShowsTheLatestItemInItsHeader()
    {
        var (window, _, group, _) = ShowTab("malandor");
        group.AddWatchedItem(Row("Morph Ball"));
        group.AddWatchedItem(Row("Screw Attack"));

        group.ToggleWatchedItemsExpandedCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var latest = Named<ContentControl>(window, "WatchedLatestItemInHeader");
        Assert.True(latest.IsVisible);
        Assert.Contains(latest.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Screw Attack");
        Assert.False(Named<ItemsControl>(window, "WatchedItemsList").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void EventRow_ForTheWatchedSlotsProgressionItem_GetsTheWatchedClass_AndTheToggleDrivesTheHighlight()
    {
        var (window, _, group, _) = ShowTab("malandor");
        var receiver = group.GetRoomSlotStatus(12);
        var entry = new EventEntry
        {
            Type = EventType.ItemReceived,
            Text = "Mira sent Varia Suit to malandor",
            Segments = [new("Varia Suit", EventTextSegmentKind.ItemProgression)],
            Receiver = receiver,
        };
        group.AppendEvent(entry);
        Dispatcher.UIThread.RunJobs();

        var list = Named<ListBox>(window, "EventsListBox");
        var row = (ListBoxItem)list.ContainerFromItem(entry)!;
        Assert.False(row.Classes.Contains("watched"));

        // A slot picked later marks older rows too.
        group.SetWatchedSlotNumber(12);
        group.ApplyWatchedSlotStatuses();
        Dispatcher.UIThread.RunJobs();

        Assert.True(row.Classes.Contains("watched"));
        Assert.True(list.Classes.Contains("highlight-watched"));

        group.HighlightWatchedEvents = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(list.Classes.Contains("highlight-watched"));
    }

    [AvaloniaFact]
    public void EyeIcon_SitsCenteredOnTheHeaderText()
    {
        var (window, _, _, _) = ShowTab("malandor");
        var strip = Named<Border>(window, "WatchedItemsStrip");
        var eye = strip.GetVisualDescendants().OfType<Path>().First(path => path.Classes.Contains("watched-eye"));
        var header = Named<TextBlock>(window, "WatchedSlotHeaderTextBlock");

        var eyeCenter = eye.TranslatePoint(new Point(0, eye.Bounds.Height / 2), strip)!.Value.Y;
        var textCenter = header.TranslatePoint(new Point(0, header.Bounds.Height / 2), strip)!.Value.Y;

        // The style nudges the eye 2px down onto the text's visual center, which moves its center by 1px.
        Assert.InRange(eyeCenter - textCenter, 0, 2);
        Assert.Equal(9, eye.Bounds.Height);
    }

    [AvaloniaFact]
    public void Editor_ListsOwnSlotsFirst_AndPreselectsTheCurrentChoice()
    {
        var group = new ServerConnectionGroup { Name = "S", WatchedSlotName = "malandor" };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        PlayerChoice P(string name) => new() { SlotName = name, DisplayText = name };

        var editor = ConnectionEditorViewModel.ForEditGroup(group, roomPlayers: [P("Mira"), P("malandor"), P("Alice")]);

        Assert.Equal(new[] { "(none)", "Alice (own slot)", "malandor", "Mira" }, editor.WatchedSlotChoices.Select(c => c.DisplayText));
        Assert.Equal("malandor", editor.SelectedWatchedSlot?.SlotName);
        Assert.False(editor.IsWatchedSlotRosterUnavailable);
    }

    [AvaloniaFact]
    public void Editor_Offline_OffersOnlyTheCurrentChoice_AndReturnsItOnSave()
    {
        var group = new ServerConnectionGroup { Name = "S", Host = "h", Port = 1, WatchedSlotName = "malandor", WatchedItemCount = 5 };

        var editor = ConnectionEditorViewModel.ForEditGroup(group);

        Assert.True(editor.IsWatchedSlotRosterUnavailable);
        Assert.Equal(new string?[] { null, "malandor" }, editor.WatchedSlotChoices.Select(c => c.SlotName));
        Assert.True(editor.TryBuildResult(out var result));
        Assert.Equal(new WatchedSlotSettings("malandor", 5, false), result.Watched);
    }

    [AvaloniaFact]
    public async Task UpdateGroup_NewWatchedSlot_GetsAFreshId_AndRefreshesTheLiveRoom()
    {
        var (_, viewModel, group, connections) = ShowTab("malandor");
        var oldId = group.Group.WatchedSlotId;

        await viewModel.UpdateGroup(group, "Server1", "host1", 12345, string.Empty, false, null,
            watched: new WatchedSlotSettings("Mira", 4, false));

        Assert.Equal("Mira", group.Group.WatchedSlotName);
        Assert.Equal(4, group.Group.WatchedItemCount);
        Assert.NotEqual(oldId, group.Group.WatchedSlotId);
        Assert.Single(connections.RefreshWatchedSlotCalls);
    }

    [AvaloniaFact]
    public async Task UpdateGroup_SameWatchedSlot_KeepsItsId_AndDoesNotRefresh()
    {
        var (_, viewModel, group, connections) = ShowTab("malandor");
        var oldId = group.Group.WatchedSlotId;

        await viewModel.UpdateGroup(group, "Server1", "host1", 12345, string.Empty, false, null,
            watched: new WatchedSlotSettings("MALANDOR", 10, false));

        Assert.Equal(oldId, group.Group.WatchedSlotId);
        Assert.Empty(connections.RefreshWatchedSlotCalls);
    }

    [AvaloniaFact]
    public void Dashboard_WatchedOnly_ListsEveryServersItems_AndHidesTheEventLog()
    {
        var connections = new FakeConnectionManager();
        var first = new GroupViewModel(new ServerConnectionGroup { Name = "A", WatchedSlotName = "malandor" }, connections);
        var second = new GroupViewModel(new ServerConnectionGroup { Name = "B", WatchedSlotName = "Mira" }, connections);
        var dashboard = new DashboardViewModel(new ObservableCollection<GroupViewModel> { first, second }, _ => { }, (_, _, _) => { });

        first.AddWatchedItem(Row("Varia Suit"));
        second.AddWatchedItem(Row("Hookshot"));
        dashboard.ToggleWatchedOnlyCommand.Execute(null);

        Assert.True(dashboard.HasWatchedSlot);
        Assert.False(dashboard.IsEventLogVisible);
        Assert.Equal(new[] { ("A", "Varia Suit"), ("B", "Hookshot") },
            dashboard.WatchedItemsAcrossGroups.Select(r => (r.Group.Group.Name, r.Item.ItemName)));

        first.Group.WatchedSlotName = null;
        second.Group.WatchedSlotName = null;

        Assert.False(dashboard.HasWatchedSlot);
        Assert.False(dashboard.ShowWatchedOnly);
    }

    [AvaloniaFact]
    public void DashboardView_ShowsTheOverviewLine_AndSwapsInTheWatchedList()
    {
        var (window, viewModel, group, _) = ShowTab("malandor");
        group.AddWatchedItem(Row("Varia Suit"));
        viewModel.IsDashboardVisible = true;
        Dispatcher.UIThread.RunJobs();

        var overviewLine = window.GetVisualDescendants().OfType<StackPanel>().First(panel => Equals(panel.Tag, "WatchedOverviewLine"));
        Assert.True(overviewLine.IsEffectivelyVisible);
        Assert.Contains(overviewLine.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "malandor: Varia Suit");

        viewModel.Dashboard.ShowEventsPanelCommand.Execute(null);
        viewModel.Dashboard.ToggleWatchedOnlyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<ListBox>(window, "DashboardEventsListBox").IsVisible);
        var watchedList = Named<ListBox>(window, "DashboardWatchedItemsListBox");
        Assert.True(watchedList.IsEffectivelyVisible);
        Assert.Single(watchedList.GetVisualDescendants().OfType<ListBoxItem>());
    }

    [AvaloniaFact]
    public void EditorWindow_ShowsTheWatchedSlotSection_InEditModeOnly()
    {
        var group = new ServerConnectionGroup { Name = "S", Host = "h", Port = 1, WatchedSlotName = "malandor" };
        var window = new ConnectionEditorWindow { DataContext = ConnectionEditorViewModel.ForEditGroup(group) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var picker = Named<ComboBox>(window, "WatchedSlotComboBox");
        Assert.True(picker.IsEffectivelyVisible);
        Assert.Equal("malandor", ((WatchedSlotChoice)picker.SelectedItem!).SlotName);

        var newGroupWindow = new ConnectionEditorWindow { DataContext = ConnectionEditorViewModel.ForNewGroup() };
        newGroupWindow.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Named<ComboBox>(newGroupWindow, "WatchedSlotComboBox").IsEffectivelyVisible);
    }
}
