using System;
using System.Collections.Generic;
using System.ComponentModel;
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
/// Kompakteres-Layout.md, Teil D: collapsible filter bars -
/// the collapsed summary texts, the expanded state shared across tabs and
/// persisted across restarts, and the real <see cref="CollapsibleFilterBar"/>
/// inside a server tab.
/// </summary>
public class CollapsibleFilterBarTests
{
    /// <summary>Like <see cref="FakePersistenceService"/>, but keeps saved settings so a restart can read them back.</summary>
    private sealed class SettingsKeepingPersistence : IPersistenceService
    {
        public AppSettings Settings { get; private set; } = new();
        public List<ServerConnectionGroup> LoadGroups() => new();
        public void SaveGroups(IEnumerable<ServerConnectionGroup> groups) { }
        public ProfileSyncState LoadSyncState(Guid slotId) => new() { ProfileId = slotId };
        public void SaveSyncState(ProfileSyncState state) { }
        public AppSettings LoadSettings() => Settings.Clone();
        public void SaveSettings(AppSettings settings) => Settings = settings.Clone();
        public DataPackageCacheEntry? LoadDataPackageCache(Guid groupId, string game) => null;
        public void SaveDataPackageCache(Guid groupId, string game, DataPackageCacheEntry entry) { }
        public void DeleteDataPackageCacheForGroup(Guid groupId) { }
    }

    private static MainWindowViewModel NewMainWindowViewModel(IPersistenceService? persistence = null) =>
        new(persistence ?? new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());

    [AvaloniaFact]
    public void EventFilterSummary_NamesOnlyWhatNarrowsTheList()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = viewModel.Groups[0];

        Assert.Equal(string.Empty, group.EventFilterSummaryText);

        group.SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;
        group.SelectedEventCategoryFilter = EventCategoryFilter.Items;
        group.ShowTrapItemEvents = false;
        group.SelectedEventsSlotFilter = group.Group.Slots[0];

        Assert.Equal("Concerns me · Items · Classes 3/4 · Alice", group.EventFilterSummaryText);
    }

    [AvaloniaFact]
    public void EventFilterSummary_RaisesPropertyChanged_WhenAFilterChanges()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = viewModel.Groups[0];
        var changed = new List<string?>();
        ((INotifyPropertyChanged)group).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        group.SelectedEventCategoryFilter = EventCategoryFilter.Chat;

        Assert.Contains(nameof(GroupViewModel.EventFilterSummaryText), changed);
    }

    [AvaloniaFact]
    public void RightPanelFilterSummary_FollowsTheActivePanel()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = viewModel.Groups[0];

        // Hints panel defaults to Unfound - that narrows the list, so it's named.
        Assert.Equal("Unfound", group.RightPanelFilterSummaryText);

        group.SelectedHintRoleFilter = HintRoleFilter.IReceive;
        group.SelectedHintItemCategoryFilter = ItemCategoryFilter.Normal;
        Assert.Equal("Unfound · My item · Filler", group.RightPanelFilterSummaryText);

        group.SelectedRightPanel = RightPanelView.ReceivedItems;
        group.ItemSearchText = "sword";
        Assert.Equal("\"sword\"", group.RightPanelFilterSummaryText);
    }

    [AvaloniaFact]
    public void DashboardSummaries_IncludeServerFilter()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var dashboard = viewModel.Dashboard;

        Assert.Equal(string.Empty, dashboard.EventFilterSummaryText);
        Assert.Equal(string.Empty, dashboard.HintFilterSummaryText);

        dashboard.EventsFilter.SelectedServer = viewModel.Groups[0];
        dashboard.SelectedHintItemCategoryFilter = ItemCategoryFilter.Trap;

        Assert.Equal(viewModel.Groups[0].HeaderText, dashboard.EventFilterSummaryText);
        Assert.Equal("Trap", dashboard.HintFilterSummaryText);
    }

    [AvaloniaFact]
    public void ExpandedState_IsSharedByEveryTabAndTheDashboard()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        viewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);

        viewModel.Groups[0].FilterLayout.EventsFiltersExpanded = false;

        Assert.False(viewModel.Groups[1].FilterLayout.EventsFiltersExpanded);
        Assert.Same(viewModel.FilterLayout, viewModel.Dashboard.FilterLayout);
    }

    [AvaloniaFact]
    public void ExpandedState_SurvivesARestart()
    {
        var persistence = new SettingsKeepingPersistence();
        var first = NewMainWindowViewModel(persistence);
        first.FilterLayout.RightPanelFiltersExpanded = false;
        first.FilterLayout.DashboardHintsFiltersExpanded = false;

        var restarted = NewMainWindowViewModel(persistence);

        Assert.False(restarted.FilterLayout.RightPanelFiltersExpanded);
        Assert.False(restarted.FilterLayout.DashboardHintsFiltersExpanded);
        Assert.True(restarted.FilterLayout.EventsFiltersExpanded);
    }

    [Fact]
    public void SettingsDialog_KeepsFilterLayoutFields()
    {
        var viewModel = SettingsViewModel.FromSettings(new AppSettings { EventsFiltersExpanded = false });
        viewModel.EventHistoryLimit = 123;

        Assert.True(viewModel.TryBuildSettings(out var saved));
        Assert.False(saved.EventsFiltersExpanded);
        Assert.Equal(123, saved.EventHistoryLimit);
    }

    [AvaloniaFact]
    public void TabEventsBar_CollapsesToSummary_AndSummaryClickExpandsAgain()
    {
        var viewModel = NewMainWindowViewModel();
        viewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 600 };
        window.Show();
        viewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var group = viewModel.Groups[0];
        group.SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;
        var slotFilter = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EventsSlotFilterComboBox");
        var bar = slotFilter.GetVisualAncestors().OfType<CollapsibleFilterBar>().First();
        var summaryButton = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_SummaryButton");
        var toggleButton = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_ToggleButton");
        Assert.True(slotFilter.IsEffectivelyVisible);
        Assert.False(summaryButton.IsVisible);

        toggleButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(group.FilterLayout.EventsFiltersExpanded);
        Assert.False(slotFilter.IsEffectivelyVisible);
        Assert.True(summaryButton.IsVisible);
        Assert.Equal("Filters: Concerns me", bar.SummaryDisplayText);

        summaryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(group.FilterLayout.EventsFiltersExpanded);
        Assert.True(slotFilter.IsEffectivelyVisible);
    }
}
