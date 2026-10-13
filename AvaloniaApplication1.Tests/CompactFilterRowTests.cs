using System.ComponentModel;
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

public class CompactFilterRowTests
{
    private static (MainWindow window, GroupViewModel group) SetUpTab(double width)
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = width, Height = 600 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();
        return (window, mainWindowViewModel.Groups[0]);
    }

    [AvaloniaFact]
    public void ItemClassFilterButtonText_ReflectsHowManyClassesAreShown()
    {
        var (_, group) = SetUpTab(900);
        var changed = new System.Collections.Generic.List<string?>();
        ((INotifyPropertyChanged)group).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("Classes", group.EventItemClassFilterButtonText);
        Assert.False(group.IsEventItemClassFilterActive);

        group.ShowTrapItemEvents = false;
        Assert.Equal("Classes 3/4", group.EventItemClassFilterButtonText);
        Assert.True(group.IsEventItemClassFilterActive);
        Assert.Contains(nameof(GroupViewModel.EventItemClassFilterButtonText), changed);
        Assert.Contains(nameof(GroupViewModel.IsEventItemClassFilterActive), changed);

        group.ShowProgressionItemEvents = false;
        group.ShowUsefulItemEvents = false;
        group.ShowFillerItemEvents = false;
        Assert.Equal("Classes 0/4", group.EventItemClassFilterButtonText);
    }

    [AvaloniaFact]
    public void TabEventsFilters_ShareOneRow_WhenThereIsRoom()
    {
        // Very wide on purpose: the headless test font renders far wider than a real one, so a realistic width would wrap.
        var (window, _) = SetUpTab(2400);

        var slotFilter = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EventsSlotFilterComboBox");
        var filterRow = slotFilter.GetVisualAncestors().OfType<WrapPanel>().First();
        var firstButton = filterRow.Children.OfType<Button>().First();

        Assert.True(filterRow.Bounds.Height < firstButton.Bounds.Height * 1.8,
            $"expected the Events filters on a single row; row height={filterRow.Bounds.Height}, button height={firstButton.Bounds.Height}");
        Assert.Contains(filterRow.Children.OfType<Button>(), b => Equals(b.Content, "Concerns me"));
    }

    [AvaloniaFact]
    public void TabHasNoEventsHeading_AndNoLooseItemClassCheckboxes()
    {
        var (window, _) = SetUpTab(1280);

        // Not counting button labels: the hidden Dashboard's "Events" switch is a Button with TextBlock content.
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Events" && !t.GetVisualAncestors().OfType<Button>().Any());
        // Checkboxes only exist inside the closed flyout.
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), c => Equals(c.Content, "Trap"));
    }

    [AvaloniaFact]
    public void TabClassesButton_HighlightsWhileAClassIsHidden()
    {
        var (window, group) = SetUpTab(1280);
        var classesButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClassesButton");
        Assert.DoesNotContain("active", classesButton.Classes);

        group.ShowFillerItemEvents = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("active", classesButton.Classes);
    }
}
