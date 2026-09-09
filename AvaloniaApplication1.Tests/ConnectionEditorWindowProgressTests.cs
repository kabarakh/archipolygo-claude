using System.Linq;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): Feature-Plaene/Archiv/Fortschrittsanzeigen.md's
/// Tier 1 per-slot progress row and the Tier 2 "Multiworld tracker" field in
/// <see cref="ConnectionEditorWindow"/>, against the real <c>.axaml</c> and a
/// real layout pass - same style as <see cref="RemoveConfiguredSlotButtonTests"/>.
/// </summary>
public class ConnectionEditorWindowProgressTests
{
    [AvaloniaFact]
    public void ConfiguredSlotRow_ProgressBar_HiddenUntilSynced_ThenShowsCorrectValues()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var synced = new SlotProfile { GroupId = group.Id, SlotName = "Alice", LocationsChecked = 30, LocationsTotal = 100 };
        var unsynced = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(synced);
        group.Slots.Add(unsynced);

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var progressBars = window.GetVisualDescendants().OfType<ProgressBar>()
            .Where(p => p.DataContext is ConfiguredSlotRow)
            .ToList();
        Assert.Equal(2, progressBars.Count);

        var syncedBar = progressBars.Single(p => ((ConfiguredSlotRow)p.DataContext!).Slot == synced);
        var unsyncedBar = progressBars.Single(p => ((ConfiguredSlotRow)p.DataContext!).Slot == unsynced);

        // IsVisible is bound directly on the row's containing Grid, not the
        // ProgressBar itself - check the actual rendered state, not just the
        // element's own (always-true-by-default) property.
        Assert.True(syncedBar.IsEffectivelyVisible, "a slot that has synced must show its progress row.");
        Assert.False(unsyncedBar.IsEffectivelyVisible, "a slot that has never synced must show no progress row at all, not a misleading 0/0.");

        Assert.Equal(100, syncedBar.Maximum);
        Assert.Equal(30, syncedBar.Value);
    }

    [AvaloniaFact]
    public void ConfiguredSlotRow_ProgressBar_AppearsLiveOnceTheSlotSyncsAfterAllRowsAreShown()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = window.GetVisualDescendants().OfType<ProgressBar>().Single(p => p.DataContext is ConfiguredSlotRow);
        Assert.False(bar.IsEffectivelyVisible);

        slot.LocationsChecked = 5;
        slot.LocationsTotal = 12;
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.IsEffectivelyVisible);
        Assert.Equal(12, bar.Maximum);
        Assert.Equal(5, bar.Value);
    }

    [AvaloniaFact]
    public void MultiworldTrackerField_Shown_ForNewGroup()
    {
        AssertTrackerFieldVisibility(ConnectionEditorViewModel.ForNewGroup(), expectedVisible: true);
    }

    [AvaloniaFact]
    public void MultiworldTrackerField_Shown_ForEditGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });

        AssertTrackerFieldVisibility(
            ConnectionEditorViewModel.ForEditGroup(group),
            expectedVisible: true);
    }

    [AvaloniaFact]
    public void MultiworldTrackerField_Hidden_ForAddSlot()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });

        AssertTrackerFieldVisibility(
            ConnectionEditorViewModel.ForAddSlot(group, new[] { new PlayerChoice { SlotName = "Bob", DisplayText = "Bob" } }),
            expectedVisible: false);
    }

    private static void AssertTrackerFieldVisibility(ConnectionEditorViewModel viewModel, bool expectedVisible)
    {
        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var trackerLabel = window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Multiworld tracker (optional)");

        Assert.Equal(expectedVisible, trackerLabel?.IsEffectivelyVisible ?? false);
    }
}
