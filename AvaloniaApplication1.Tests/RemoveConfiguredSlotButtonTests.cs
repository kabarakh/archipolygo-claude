using System.Linq;
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
/// Kategorie C (Test-Umsetzungsplan.md): clicking "✕" in <see cref="ConnectionEditorWindow"/>'s
/// slot-management list must only *stage* a removal - not remove/disconnect
/// the slot right away. A real simulated click, wired via code-behind
/// (<c>OnRemoveConfiguredSlotClick</c>) rather than calling the command
/// property directly, so a future edit that gets the generated command name
/// wrong there would actually be caught by this test (<c>[RelayCommand]</c>
/// on the now-synchronous <c>RemoveConfiguredSlot</c> still generates
/// <c>RemoveConfiguredSlotCommand</c> - see CLAUDE.md's trailing-"Async"-
/// stripping gotcha, which only applies to the async case, not this one).
///
/// This staging behavior itself exists because the old design - removing
/// (and disconnecting, if it was the live leader) the instant "✕" was
/// clicked - meant "make a different slot the default, then remove the old
/// leader" in the same Edit Server session could disconnect the user before
/// they'd even clicked Save, with no way to reconsider via Cancel. See
/// <see cref="MainWindowViewModel.UpdateGroup"/>'s doc comment for where the
/// actual removal now happens instead.
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
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var removeButtonForBob = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "✕") && (b.DataContext as ConfiguredSlotRow)?.Slot == removedSlot);

        // Bounds.Center is in the button's own parent's coordinate space, not
        // the button's local space - translate a local-space center point
        // instead (Bounds.Width/Height are the same regardless of space).
        var localCenter = new Point(removeButtonForBob.Bounds.Width / 2, removeButtonForBob.Bounds.Height / 2);
        var pointInWindow = removeButtonForBob.TranslatePoint(localCenter, window) ?? localCenter;
        window.MouseDown(pointInWindow, MouseButton.Left);
        window.MouseUp(pointInWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // The row disappears from the dialog's own list right away (visible
        // feedback that the click did something)...
        Assert.DoesNotContain(viewModel.ConfiguredSlotRows, r => r.Slot == removedSlot);
        Assert.Contains(viewModel.ConfiguredSlotRows, r => r.Slot == keptSlot);

        // ...but the slot itself is untouched in the real, live configuration -
        // nothing has actually removed it (or disconnected anything) yet.
        Assert.Contains(removedSlot, group.Slots);

        // The removal is only staged, to be applied on Save.
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
}
