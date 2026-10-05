using System.Linq;
using Archipolygo.Models;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie C (Test-Umsetzungsplan.md): on a short screen (e.g. a 2560x720
/// ultrawide), <see cref="DialogScreenFit"/> caps a dialog's height to the
/// screen's working area. These check the other half of that fix - that the
/// real <c>.axaml</c> then scrolls its body instead of clipping, so Save stays
/// fully inside the window. The cap itself is set directly here (the headless
/// platform has no real screen to measure).
/// </summary>
public class DialogShortScreenTests
{
    private const double CappedHeight = 300;

    [AvaloniaFact]
    public void SettingsWindow_CappedHeight_SaveButtonStaysInsideWindow()
    {
        var window = new SettingsWindow
        {
            DataContext = SettingsViewModel.FromSettings(new AppSettings()),
            MaxHeight = CappedHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        AssertButtonFullyInside(window, "Save");
    }

    [AvaloniaFact]
    public void ConnectionEditorWindow_EditGroup_CappedHeight_SaveButtonStaysInsideWindow()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1, IsAdmin = true };
        for (var i = 0; i < 6; i++)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = $"Slot {i}", LocationsChecked = i, LocationsTotal = 10 });
        }

        var viewModel = ConnectionEditorViewModel.ForEditGroup(group);
        var window = new ConnectionEditorWindow { DataContext = viewModel, MaxHeight = CappedHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        AssertButtonFullyInside(window, viewModel.SaveButtonText);

        // One scrollbar for the whole dialog - the slot list must not bring
        // its own nested ScrollViewer back (TextBoxes' template-internal ones
        // don't count, hence the TemplatedParent filter).
        var ownScrollViewers = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(s => s.TemplatedParent is null);
        Assert.Single(ownScrollViewers);
    }

    private static void AssertButtonFullyInside(Window window, string content)
    {
        Assert.True(window.Bounds.Height <= CappedHeight, $"window grew to {window.Bounds.Height}px past its {CappedHeight}px cap.");

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, content));
        Assert.True(button.IsEffectivelyVisible);

        var bottomRight = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), window);
        Assert.NotNull(bottomRight);
        Assert.True(bottomRight!.Value.Y <= window.Bounds.Height,
            $"\"{content}\" ends at y={bottomRight.Value.Y}, below the window's {window.Bounds.Height}px height.");

        // The body above it must actually be the part that got squeezed into
        // a scrolling area, not just silently clipped.
        var scrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
    }
}
