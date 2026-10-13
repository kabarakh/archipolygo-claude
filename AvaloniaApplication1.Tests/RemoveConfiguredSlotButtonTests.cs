using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
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
/// A real click through the code-behind handler, so a wrong generated command name would be caught.
/// Removal is only staged (applied on Save) so removing the live leader can't disconnect before the user saves.
/// </summary>
public class RemoveConfiguredSlotButtonTests
{
    [AvaloniaFact]
    public void ClickingRemoveButton_HidesTheRow_ButStagesRemovalInsteadOfActingImmediately()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var keptSlot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        var removedSlot = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(keptSlot);
        group.Slots.Add(removedSlot);

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);

        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.ShowConfirmationDialogAsync = _ => Task.FromResult(true);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var removeButtonForBob = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "✕") && (b.DataContext as ConfiguredSlotRow)?.Slot == removedSlot);

        // Bounds.Center is in the parent's coordinate space; use a local-space center.
        var localCenter = new Point(removeButtonForBob.Bounds.Width / 2, removeButtonForBob.Bounds.Height / 2);
        var pointInWindow = removeButtonForBob.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(viewModel.ConfiguredSlotRows, r => r.Slot == removedSlot);
        Assert.Contains(viewModel.ConfiguredSlotRows, r => r.Slot == keptSlot);

        // ...but the live configuration is untouched until Save.
        Assert.Contains(removedSlot, group.Slots);

        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.Contains(removedSlot, result.SlotsToRemove);
        Assert.DoesNotContain(keptSlot, result.SlotsToRemove);
    }

    [AvaloniaFact]
    public void RemovingTheCurrentDefaultLeader_ClearsThatPreference()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var defaultSlot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(defaultSlot);
        group.PreferredLeaderSlotId = defaultSlot.Id;

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);

        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.ShowConfirmationDialogAsync = _ => Task.FromResult(true);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var removeButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "✕") && (b.DataContext as ConfiguredSlotRow)?.Slot == defaultSlot);
        var localCenter = new Point(removeButton.Bounds.Width / 2, removeButton.Bounds.Height / 2);
        var pointInWindow = removeButton.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.Null(result.PreferredLeaderSlotId);
    }

    [AvaloniaFact]
    public void DecliningTheConfirmation_LeavesTheRowAndSlotUntouched()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);

        var window = new ConnectionEditorWindow { DataContext = viewModel };
        window.ShowConfirmationDialogAsync = _ => Task.FromResult(false);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var removeButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "✕") && (b.DataContext as ConfiguredSlotRow)?.Slot == slot);
        var localCenter = new Point(removeButton.Bounds.Width / 2, removeButton.Bounds.Height / 2);
        var pointInWindow = removeButton.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(viewModel.ConfiguredSlotRows, r => r.Slot == slot);
        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.DoesNotContain(slot, result.SlotsToRemove);
    }
}
