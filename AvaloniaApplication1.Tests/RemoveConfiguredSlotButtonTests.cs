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
/// Kategorie C (Test-Umsetzungsplan.md): the <c>[RelayCommand]</c>
/// trailing-"Async"-stripping gotcha from CLAUDE.md - the generated command
/// property is <c>RemoveConfiguredSlotCommand</c>, not
/// <c>RemoveConfiguredSlotAsyncCommand</c>, for the method
/// <c>RemoveConfiguredSlotAsync</c>. A real simulated click on the "✕" button
/// in <see cref="ConnectionEditorWindow"/>'s slot-management list, wired via
/// code-behind (<c>OnRemoveConfiguredSlotClick</c>, not a direct Command
/// binding - see that handler's doc comment) rather than calling the command
/// property directly, so a future edit that gets the generated command name
/// wrong there would actually be caught by this test.
/// </summary>
public class RemoveConfiguredSlotButtonTests
{
    [AvaloniaFact]
    public void ClickingRemoveButton_InvokesRemoveConfiguredSlotCommand_AndRemovesTheRow()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var keptSlot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        var removedSlot = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(keptSlot);
        group.Slots.Add(removedSlot);

        SlotProfile? removedViaCallback = null;
        var viewModel = ConnectionEditorViewModel.ForEditGroup(
            group,
            removeSlotAsync: slot =>
            {
                removedViaCallback = slot;
                return System.Threading.Tasks.Task.CompletedTask;
            });

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

        Assert.Same(removedSlot, removedViaCallback);
        Assert.DoesNotContain(viewModel.ConfiguredSlotRows, r => r.Slot == removedSlot);
        Assert.Contains(viewModel.ConfiguredSlotRows, r => r.Slot == keptSlot);
    }
}
