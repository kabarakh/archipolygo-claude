using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;
/// <summary>
/// Owns every server (<see cref="Groups"/>), which are docked as tabs, startup
/// sequencing and app-wide settings. Split by topic into <c>MainWindowViewModel.*.cs</c>.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPersistenceService _persistenceService;
    private readonly IConnectionManager _connectionManager;
    private readonly IMultiworldTrackerService _multiworldTrackerService;

    /// <summary>Optional (null in tests): update checks and actions then silently no-op.</summary>
    private readonly IUpdateService? _updateService;

    /// <summary>Only read here (for the Settings export); the services do the writing.</summary>
    private readonly IDiagnosticLogger _diagnosticLogger;

    /// <summary>
    /// Set by the view to show the generic "are you sure?" dialog. Null (tests) = no
    /// dialog, act immediately.
    /// </summary>
    public Func<ConfirmationViewModel, Task<bool>>? ShowConfirmationDialogAsync { get; set; }

    public ObservableCollection<GroupViewModel> Groups { get; } = new();

    /// <summary>
    /// Groups shown as tabs (all but detached ones). A separately maintained mirror, not a
    /// passthrough: <see cref="Groups"/> stays the source of truth, and every site that changes
    /// its membership/order mirrors the change here. Detaching is never persisted.
    /// </summary>
    public ObservableCollection<GroupViewModel> DockedGroups { get; } = new();

    [ObservableProperty]
    private GroupViewModel? _selectedGroup;

    /// <summary>Dashboard shown instead of the TabControl (a view swap, not a real TabItem). The app opens on it.</summary>
    [ObservableProperty]
    private bool _isDashboardVisible = true;

    /// <summary>Created once, not per toggle, so its filter state survives toggling.</summary>
    public DashboardViewModel Dashboard { get; }

    public string DashboardToggleButtonText => IsDashboardVisible ? "Tab View" : "Dashboard";

    partial void OnIsDashboardVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(DashboardToggleButtonText));

        // Leaving the Dashboard makes the selected tab visible again.
        _attentionTracker?.RefreshSeenState();
    }

    [RelayCommand]
    private void ToggleDashboard() => IsDashboardVisible = !IsDashboardVisible;

    private GroupViewModel CreateGroupViewModel(ServerConnectionGroup group) =>
        new(group, _connectionManager, _multiworldTrackerService, FilterLayout)
        {
            ShowUnreadInTitle = _showUnreadInTitle,
            ShowUnreadBadge = _showUnreadBadge
        };

    /// <summary>Design-time only (XAML previewer); at runtime <see cref="App"/> resolves this via DI.</summary>
    public MainWindowViewModel()
        : this(new PersistenceService(), CreateDesignTimeConnectionManager(), new MultiworldTrackerService(), new UpdateService())
    {
    }

    private static IConnectionManager CreateDesignTimeConnectionManager()
    {
        var syncStateStore = new ProfileSyncStateStore(new PersistenceService());
        return new ConnectionManager(
            new MessageHistoryService(new PersistenceService(), syncStateStore),
            new HintService(syncStateStore),
            new ArchipelagoSessionFactoryAdapter());
    }

    public MainWindowViewModel(IPersistenceService persistenceService, IConnectionManager connectionManager, IMultiworldTrackerService multiworldTrackerService, IUpdateService? updateService = null, IDiagnosticLogger? diagnosticLogger = null, IAttentionTracker? attentionTracker = null,
        DataDirectory? dataDirectory = null, IConfigExportService? configExportService = null)
    {
        _dataDirectory = dataDirectory;
        _configExportService = configExportService;
        _attentionTracker = attentionTracker;
        _persistenceService = persistenceService;
        _connectionManager = connectionManager;
        _multiworldTrackerService = multiworldTrackerService;
        _updateService = updateService;
        _diagnosticLogger = diagnosticLogger ?? NullDiagnosticLogger.Instance;

        SubscribeToConnectionManager();

        var startupSettings = _persistenceService.LoadSettings();
        FilterLayout = LoadFilterLayout(startupSettings);
        FilterLayout.PropertyChanged += (_, _) => SaveFilterLayout();
        _showUnreadInTitle = startupSettings.ShowUnreadInTitle;
        _showUnreadBadge = startupSettings.ShowUnreadBadge;

        Groups.CollectionChanged += OnGroupsChanged;
        DockedGroups.CollectionChanged += (_, _) => RaiseUnreadDisplayChanged();
        LoadGroupsFromDisk();

        Dashboard = new DashboardViewModel(Groups, SelectGroupAndLeaveDashboard, ReorderGroup, FilterLayout);
        SelectedGroup = Groups.FirstOrDefault();

        _ = InitializeGroupsAsync();

        // Silent fire-and-forget: the dot next to "Settings..." just appears or doesn't.
        _ = CheckForUpdatesAsync();

        // App.axaml.cs already applied the theme; this only feeds the theme button.
        ThemePreference = startupSettings.ThemePreference;
    }

    private void SubscribeToConnectionManager()
    {
        // Raised on the UI thread, so enumerating Groups is safe.
        _connectionManager.GroupPersistNeeded += _ => PersistGroups();

        _connectionManager.SlotSyncBatchStarting += OnSlotSyncBatchStarting;
        _connectionManager.SlotInitialSyncCompleted += (_, _) => StartupSyncCompleted++;

        _connectionManager.PasswordRequested = HandlePasswordRequestedAsync;
    }

    /// <summary>Every group as last saved, all docked - detaching is never persisted.</summary>
    private void LoadGroupsFromDisk()
    {
        foreach (var group in _persistenceService.LoadGroups())
        {
            var groupViewModel = CreateGroupViewModel(group);
            Groups.Add(groupViewModel);
            DockedGroups.Add(groupViewModel);
        }
    }

    private void SelectGroupAndLeaveDashboard(GroupViewModel group)
    {
        SelectedGroup = group;
        IsDashboardVisible = false;
    }

    partial void OnSelectedGroupChanged(GroupViewModel? oldValue, GroupViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        _attentionTracker?.RefreshSeenState();
    }

    // Sync counts come from each connect's own SlotSyncBatchStarting; don't force
    // Completed = Total afterwards, it could cut off an overlapping later pass.
    private Task InitializeGroupsAsync() =>
        StartSpacedAsync(Groups.ToList(), _connectionManager.InitializeGroupAsync);

    /// <summary>
    /// Starts one connect per group, <see cref="ConnectionManager.StartupGroupSpacing"/> apart, without
    /// waiting for the previous one: ConnectionManager already runs handshakes one at a time, and an
    /// open password prompt for one server must not hold up the others.
    /// </summary>
    private static async Task StartSpacedAsync(IReadOnlyList<GroupViewModel> groups, Func<GroupViewModel, Task> connect)
    {
        var connects = new List<Task>();
        for (var i = 0; i < groups.Count; i++)
        {
            connects.Add(connect(groups[i]));

            if (i < groups.Count - 1)
            {
                await Task.Delay(ConnectionManager.StartupGroupSpacing);
            }
        }

        await Task.WhenAll(connects);
    }

    /// <summary>Used by the connection editor to detect duplicate Host+Port / slot names.</summary>
    public IReadOnlyList<ServerConnectionGroup> GetAllGroups() => Groups.Select(g => g.Group).ToList();

    /// <summary>Filter bars' expanded state, shared by every tab and the Dashboard.</summary>
    public FilterBarLayoutState FilterLayout { get; }

    private static FilterBarLayoutState LoadFilterLayout(AppSettings settings) => new()
    {
        EventsFiltersExpanded = settings.EventsFiltersExpanded,
        RightPanelFiltersExpanded = settings.RightPanelFiltersExpanded,
        DashboardEventsFiltersExpanded = settings.DashboardEventsFiltersExpanded,
        DashboardHintsFiltersExpanded = settings.DashboardHintsFiltersExpanded
    };

    /// <summary>Saved on every toggle so collapsed filters stay collapsed across restarts.</summary>
    private void SaveFilterLayout()
    {
        var settings = _persistenceService.LoadSettings();
        settings.EventsFiltersExpanded = FilterLayout.EventsFiltersExpanded;
        settings.RightPanelFiltersExpanded = FilterLayout.RightPanelFiltersExpanded;
        settings.DashboardEventsFiltersExpanded = FilterLayout.DashboardEventsFiltersExpanded;
        settings.DashboardHintsFiltersExpanded = FilterLayout.DashboardHintsFiltersExpanded;
        _persistenceService.SaveSettings(settings);
    }

    public AppSettings LoadSettings() => _persistenceService.LoadSettings();

    /// <summary>Persists the Settings dialog's result and applies it right away, no restart needed.</summary>
    public void SaveSettings(AppSettings settings)
    {
        _persistenceService.SaveSettings(settings);
        DensityService.Apply(settings.UiDensity);
        _attentionTracker?.ApplySettings(settings);
        ApplyUnreadDisplaySettings(settings);
    }

    public string ReadDiagnosticLog() => _diagnosticLogger.ReadAll();

    private void PersistGroups() => _persistenceService.SaveGroups(GetAllGroups());
}
