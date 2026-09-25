using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): the "Copy from here" buttons
/// (Feature-Plaene/Log-Export.md) - visibility tracking the list's
/// selection (floating over the list since Kompakteres-Layout.md), and the actual clipboard content after a click. Real
/// <see cref="MainWindow"/>, same setup style as <see cref="EventsListAutoScrollTests"/>.
/// </summary>
public class CopyFromHereTests
{
    private static (MainWindow window, GroupViewModel group, ListBox listBox, Button copyButton) SetUp(string listBoxName, string copyButtonName)
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];

        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();

        // Tab content only materializes once visible - see MainWindow.axaml's
        // lazy ContentTemplate gotcha (CLAUDE.md), same as EventsListAutoScrollTests.
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var listBox = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == listBoxName);
        var copyButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == copyButtonName);

        return (window, group, listBox, copyButton);
    }

    private static void Click(MainWindow window, Control control)
    {
        var localCenter = new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var pointInWindow = control.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void EventsCopyFromHereButton_HiddenUntilARowIsSelected()
    {
        var (_, group, listBox, copyButton) = SetUp("EventsListBox", "CopyEventsFromHereButton");
        group.Events.Add(new EventEntry { Text = "Event 0", Type = EventType.Chat });
        group.Events.Add(new EventEntry { Text = "Event 1", Type = EventType.Chat });
        Dispatcher.UIThread.RunJobs();

        Assert.False(copyButton.IsVisible, "expected no selection yet");

        listBox.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(copyButton.IsVisible);

        listBox.SelectedIndex = -1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(copyButton.IsVisible, "expected hidden again once the selection is cleared");
    }

    [AvaloniaFact]
    public async Task EventsCopyFromHereButton_CopiesTheSelectedEventAndEveryOneAfterIt()
    {
        var (window, group, listBox, copyButton) = SetUp("EventsListBox", "CopyEventsFromHereButton");
        for (var i = 0; i < 4; i++)
        {
            group.Events.Add(new EventEntry { Text = $"Event {i}", Type = EventType.Chat });
        }
        Dispatcher.UIThread.RunJobs();

        listBox.SelectedIndex = 1; // "Event 1" - everything from here onward, "Event 0" excluded.
        Dispatcher.UIThread.RunJobs();

        Click(window, copyButton);

        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)!.Clipboard!;
        var text = await clipboard.TryGetTextAsync();
        Assert.Equal("Event 1\r\nEvent 2\r\nEvent 3", text?.ReplaceLineEndings("\r\n"));
    }

    [AvaloniaFact]
    public async Task HintsCopyFromHereButton_CopiesTheSelectedHintAndEveryOneAfterIt()
    {
        var (window, group, listBox, copyButton) = SetUp("HintsListBox", "CopyHintsFromHereButton");

        var slotId = group.Group.Slots[0].Id;
        group.Hints.Add(new HintEntry { Key = "hint-0", SlotId = slotId, ItemName = "Sword", FindingPlayerName = "Alice", ReceivingPlayerName = "Bob", LocationName = "Chest 0" });
        group.Hints.Add(new HintEntry { Key = "hint-1", SlotId = slotId, ItemName = "Shield", FindingPlayerName = "Alice", ReceivingPlayerName = "Bob", LocationName = "Chest 1" });
        group.SelectedHintFilter = HintFilter.All;
        Dispatcher.UIThread.RunJobs();

        listBox.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Click(window, copyButton);

        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)!.Clipboard!;
        var text = await clipboard.TryGetTextAsync();
        Assert.Equal("Shield: Alice -> Bob : Chest 1", text);
    }
}
