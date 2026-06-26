using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IPersistenceService _persistenceService;
    private readonly IConnectionManager _connectionManager;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private TabViewModel? _selectedTab;

    /// <summary>
    /// Design-time only: used by the <c>&lt;Design.DataContext&gt;</c> in
    /// MainWindow.axaml so the XAML previewer has something to bind against.
    /// Not used at runtime - see <see cref="App"/> for the actual composition
    /// root, which resolves this view model (and its dependencies) from the
    /// DI container as explicit singletons instead.
    /// </summary>
    public MainWindowViewModel()
        : this(new PersistenceService(), CreateDesignTimeConnectionManager())
    {
    }

    /// <summary>
    /// Wires up a design-time-only <see cref="IConnectionManager"/> using one
    /// shared <see cref="ProfileSyncStateStore"/> instance for both
    /// dependencies, the same way <see cref="App"/> does via the real DI
    /// container - HintService and MessageHistoryService must not each get
    /// their own independently-loaded sync-state cache, or whichever saves
    /// last would overwrite the other's already-persisted field.
    /// </summary>
    private static IConnectionManager CreateDesignTimeConnectionManager()
    {
        var syncStateStore = new ProfileSyncStateStore(new PersistenceService());
        return new ConnectionManager(
            new MessageHistoryService(new PersistenceService(), syncStateStore),
            new HintService(syncStateStore));
    }

    /// <summary>
    /// Real constructor, resolved by the DI container in <see cref="App"/>.
    /// Also directly usable by tests that need to substitute either
    /// dependency with a fake/mock.
    /// </summary>
    public MainWindowViewModel(IPersistenceService persistenceService, IConnectionManager connectionManager)
    {
        _persistenceService = persistenceService;
        _connectionManager = connectionManager;

        foreach (var profile in _persistenceService.LoadProfiles())
        {
            Tabs.Add(new TabViewModel(profile, _connectionManager));
        }

        RegroupTabs();

        SelectedTab = Tabs.Count > 0 ? Tabs[0] : null;

        foreach (var tab in Tabs)
        {
            if (tab.ServerProfile.AutoConnect)
            {
                _ = _connectionManager.ConnectAsync(tab);
            }
        }
    }

    /// <summary>
    /// Keeps each tab's IsSelected flag in sync with the active tab so that
    /// only the active tab's incoming events are excluded from the unread
    /// marker (see <see cref="TabViewModel.HasUnreadEvents"/>).
    /// </summary>
    partial void OnSelectedTabChanged(TabViewModel? oldValue, TabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    /// <summary>
    /// Adds a newly created or edited profile, or updates the existing tab,
    /// and persists the profile list.
    /// </summary>
    public void AddOrUpdateProfile(ServerProfile profile)
    {
        var existingTab = FindTabByProfileId(profile.Id);
        if (existingTab is not null)
        {
            existingTab.ServerProfile = profile;
        }
        else
        {
            var tab = new TabViewModel(profile, _connectionManager);
            Tabs.Add(tab);
            SelectedTab = tab;
        }

        // Host/Port may have changed (new profile, or an edit), so tabs may
        // need to move to stay grouped by Host+Port.
        RegroupTabs();

        PersistProfiles();
    }

    /// <summary>
    /// All currently known profiles (one per tab); used by the connection
    /// editor to detect exact Host+Port+SlotName duplicates, e.g. when
    /// duplicating a connection.
    /// </summary>
    public IReadOnlyList<ServerProfile> GetAllProfiles() => Tabs.Select(t => t.ServerProfile).ToList();

    public AppSettings LoadSettings() => _persistenceService.LoadSettings();

    public void SaveSettings(AppSettings settings) => _persistenceService.SaveSettings(settings);

    /// <summary>
    /// Disconnects every tab that is currently connected/connecting; each
    /// disconnect sticks (no AutoConnect bring-back) until the user
    /// reconnects that tab manually, same as the per-tab Disconnect button.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAllTabsAsync()
    {
        var tabsToDisconnect = Tabs.Where(t => t.CanDisconnect).ToList();
        foreach (var tab in tabsToDisconnect)
        {
            await _connectionManager.DisconnectAsync(tab);
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedTabAsync()
    {
        if (SelectedTab is null)
        {
            return;
        }

        var tabToRemove = SelectedTab;

        if (tabToRemove.CanDisconnect)
        {
            await _connectionManager.DisconnectAsync(tabToRemove);
        }

        var index = Tabs.IndexOf(tabToRemove);
        Tabs.Remove(tabToRemove);

        SelectedTab = Tabs.Count > 0 ? Tabs[Math.Min(index, Tabs.Count - 1)] : null;

        PersistProfiles();
    }

    private TabViewModel? FindTabByProfileId(Guid id) =>
        Tabs.FirstOrDefault(t => t.ServerProfile.Id == id);

    /// <summary>
    /// Reorders <see cref="Tabs"/> in place so that tabs sharing the same
    /// Host+Port are always adjacent, while otherwise preserving relative
    /// order as much as possible (LINQ's GroupBy is stable, and so is this
    /// loop, since it always moves a tab forward to its target slot without
    /// touching the relative order of the tabs after it).
    /// <see cref="ObservableCollection{T}.Move"/> is used instead of a
    /// clear-and-rebuild so that <see cref="SelectedTab"/> and the
    /// TabControl's selection are not disturbed.
    /// </summary>
    private void RegroupTabs()
    {
        var desiredOrder = Tabs
            .GroupBy(t => (t.ServerProfile.Host, t.ServerProfile.Port))
            .SelectMany(group => group)
            .ToList();

        for (var targetIndex = 0; targetIndex < desiredOrder.Count; targetIndex++)
        {
            var tab = desiredOrder[targetIndex];
            var currentIndex = Tabs.IndexOf(tab);
            if (currentIndex != targetIndex)
            {
                Tabs.Move(currentIndex, targetIndex);
            }
        }
    }

    private void PersistProfiles() => _persistenceService.SaveProfiles(GetAllProfiles());
}
