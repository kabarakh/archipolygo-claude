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
    private readonly IMultiworldTrackerService _multiworldTrackerService;

    /// <summary>
    /// Optional - null in every existing test construction site that doesn't
    /// care about Feature-Plaene/Archiv/Auto-Update.md, same "purely additive
    /// dependency" reasoning as <see cref="GroupViewModel"/>'s own
    /// <c>IMultiworldTrackerService?</c>. Null just means the startup check
    /// and "Update now"/"Check for updates" actions all silently no-op.
    /// </summary>
    private readonly IUpdateService? _updateService;

    public ObservableCollection<GroupViewModel> Groups { get; } = new();

    [ObservableProperty]
    private GroupViewModel? _selectedGroup;

    /// <summary>
    /// Total number of configured slots announced so far by whatever
    /// <see cref="IConnectionManager.SwitchLeaderAsync"/> sync pass(es) are
    /// currently in flight - see <see cref="IConnectionManager.SlotSyncBatchStarting"/>,
    /// which this keeps growing by (reset back to 0 first if nothing is
    /// currently pending, so a long-finished earlier pass isn't carried
    /// over). Not just the startup pass despite the name - the same banner
    /// reappears for any later reconnect too, e.g. manually reconnecting a
    /// group that was left disconnected.
    /// </summary>
    [ObservableProperty]
    private int _startupSyncTotal;

    /// <summary>How many of <see cref="StartupSyncTotal"/> have finished so far - see <see cref="IConnectionManager.SlotInitialSyncCompleted"/>.</summary>
    [ObservableProperty]
    private int _startupSyncCompleted;

    /// <summary>Whether the sync status banner should be visible - true only while there's still at least one unfinished slot from a pass that actually found any.</summary>
    public bool IsStartupSyncing => StartupSyncTotal > 0 && StartupSyncCompleted < StartupSyncTotal;

    public string StartupSyncStatusText => $"Catching up slots: {StartupSyncCompleted}/{StartupSyncTotal}";

    partial void OnStartupSyncTotalChanged(int value)
    {
        OnPropertyChanged(nameof(IsStartupSyncing));
        OnPropertyChanged(nameof(StartupSyncStatusText));
    }

    partial void OnStartupSyncCompletedChanged(int value)
    {
        OnPropertyChanged(nameof(IsStartupSyncing));
        OnPropertyChanged(nameof(StartupSyncStatusText));
    }

    /// <summary>
    /// Design-time only: used by the <c>&lt;Design.DataContext&gt;</c> in
    /// MainWindow.axaml so the XAML previewer has something to bind against.
    /// Not used at runtime - see <see cref="App"/> for the actual composition
    /// root, which resolves this view model (and its dependencies) from the
    /// DI container as explicit singletons instead.
    /// </summary>
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

    /// <summary>
    /// Real constructor, resolved by the DI container in <see cref="App"/>.
    /// Also directly usable by tests that need to substitute either
    /// dependency with a fake/mock.
    /// </summary>
    public MainWindowViewModel(IPersistenceService persistenceService, IConnectionManager connectionManager, IMultiworldTrackerService multiworldTrackerService, IUpdateService? updateService = null)
    {
        _persistenceService = persistenceService;
        _connectionManager = connectionManager;
        _multiworldTrackerService = multiworldTrackerService;
        _updateService = updateService;

        // Keeps AutoConnect/PreferredLeaderSlotId changes made by
        // ConnectionManager itself (see IConnectionManager.GroupPersistNeeded)
        // durable across a restart - e.g. connecting a leader via the account
        // dropdown, not just the actions already routed through this class's
        // own PersistGroups() calls below. ConnectionManager always raises
        // this on the UI thread, so it's safe to enumerate Groups here.
        _connectionManager.GroupPersistNeeded += _ => PersistGroups();

        // Drives the "Catching up slots: N/M" banner for every sync pass -
        // not just the initial startup one, but any later reconnect too
        // (see IConnectionManager.SlotSyncBatchStarting's doc comment).
        // Resets back to a fresh 0/0 first if nothing is currently pending,
        // so a long-finished earlier pass's numbers aren't carried over into
        // this new one; otherwise just extends the running total, so two
        // passes overlapping (e.g. a manual reconnect while the startup pass
        // is still processing a different group) don't clobber each other's
        // progress. Both events are always raised on the UI thread, so it's
        // safe to update these observable properties directly from either.
        _connectionManager.SlotSyncBatchStarting += count =>
        {
            if (StartupSyncCompleted >= StartupSyncTotal)
            {
                StartupSyncTotal = 0;
                StartupSyncCompleted = 0;
            }

            StartupSyncTotal += count;
        };
        _connectionManager.SlotInitialSyncCompleted += (_, _) => StartupSyncCompleted++;

        foreach (var group in _persistenceService.LoadGroups())
        {
            Groups.Add(new GroupViewModel(group, _connectionManager, _multiworldTrackerService));
        }

        SelectedGroup = Groups.Count > 0 ? Groups[0] : null;

        // Sequential on purpose, with a spacing delay between groups (see
        // ConnectionManager.StartupGroupSpacing) - several servers each
        // auto-connecting their leader and then catching up every other
        // configured slot at once would otherwise hit all of them with a
        // burst of simultaneous handshakes.
        _ = InitializeGroupsAsync();

        // Fire-and-forget, after everything else above - never blocks
        // startup, never crashes it either (see IUpdateService's own "never
        // throws" contract). Deliberately no visible "checking..." state for
        // this one - unlike the Settings dialog's own "Check for updates"
        // button, nobody explicitly asked for this to happen, so there's
        // nothing to show progress for; the small dot next to "Settings..."
        // (see MainWindow.axaml) either quietly appears once this resolves,
        // or it doesn't.
        _ = CheckForUpdatesAsync();
    }

    /// <summary>Whether a new version is available - drives the small dot next to the "Settings..." button in MainWindow.axaml.</summary>
    [ObservableProperty]
    private bool _isUpdateAvailable;

    /// <summary>The new version's display string, if <see cref="IsUpdateAvailable"/> - shown in the update Flyout.</summary>
    [ObservableProperty]
    private string? _newUpdateVersion;

    /// <summary>
    /// Whether to show the Settings dialog's "you're on an unmanaged install"
    /// hint (see <see cref="SettingsViewModel.ShowUnmanagedInstallHint"/>) -
    /// true only when auto-update could plausibly work here (this platform
    /// has a Velopack package at all - see <see cref="IUpdateService.SupportsManagedInstall"/>,
    /// false on macOS) but doesn't right now, because this particular install
    /// isn't Velopack-managed (a manually downloaded/unzipped build - see
    /// <see cref="IUpdateService.IsManagedInstall"/>). Never changes for the
    /// life of the running process, same as <see cref="AppVersionInfo.Current"/> -
    /// no <c>[ObservableProperty]</c> needed.
    /// </summary>
    public bool ShowUnmanagedInstallHint =>
        _updateService is not null && _updateService.SupportsManagedInstall && !_updateService.IsManagedInstall;

    /// <summary>
    /// Checks for an update (see <see cref="IUpdateService.CheckForUpdatesAsync"/>)
    /// and updates <see cref="IsUpdateAvailable"/>/<see cref="NewUpdateVersion"/>
    /// accordingly - called once at startup, and again from the Settings
    /// dialog's own "Check for updates" button, so either path keeps the
    /// badge current. No-op (never available) if no <see cref="IUpdateService"/>
    /// was supplied at all.
    /// </summary>
    public async Task<string?> CheckForUpdatesAsync()
    {
        if (_updateService is null)
        {
            return null;
        }

        var version = await _updateService.CheckForUpdatesAsync();
        NewUpdateVersion = version;
        IsUpdateAvailable = version is not null;
        return version;
    }

    /// <summary>Downloads and applies whatever update was last found, restarting the app into it - see <see cref="IUpdateService.DownloadAndApplyUpdateAsync"/>.</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_updateService is null)
        {
            return;
        }

        await _updateService.DownloadAndApplyUpdateAsync();
    }

    /// <summary>
    /// Keeps each group's IsSelected flag in sync with the active tab so
    /// that only the active tab's incoming events are excluded from the
    /// unread marker (see <see cref="GroupViewModel.HasUnreadEvents"/>).
    /// </summary>
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
    }

    private async Task InitializeGroupsAsync()
    {
        var groupsSnapshot = Groups.ToList();

        // No upfront StartupSyncTotal here anymore - each group's own
        // IConnectionManager.SwitchLeaderAsync call (via InitializeGroupAsync)
        // announces its own slot counts as it goes (see
        // IConnectionManager.SlotSyncBatchStarting), each one already
        // guaranteed a matching completion no matter how it ends. That's
        // also what lets the exact same banner reappear for a later ad-hoc
        // reconnect, not just this startup pass - so forcing
        // StartupSyncCompleted = StartupSyncTotal here once this loop
        // finishes would risk wrongly cutting off a still-in-progress later
        // pass that happens to overlap the tail end of this one.
        for (var i = 0; i < groupsSnapshot.Count; i++)
        {
            await _connectionManager.InitializeGroupAsync(groupsSnapshot[i]);

            if (i < groupsSnapshot.Count - 1)
            {
                await Task.Delay(ConnectionManager.StartupGroupSpacing);
            }
        }
    }

    /// <summary>
    /// Creates a brand-new server (group) with its first slot, and connects
    /// that slot as the group's leader right away - it's the only slot
    /// there is, so there's nothing to choose between, and connecting
    /// immediately also means <see cref="GroupViewModel.SelectedChatSlot"/>
    /// (the "Chat as:" dropdown) ends up defaulting to it via the normal
    /// leader-switch bookkeeping (see <see cref="IConnectionManager.SwitchLeaderAsync"/>).
    /// This is separate from <paramref name="autoConnect"/>, which only
    /// controls whether this group reconnects automatically at the *next*
    /// app start (or after an unexpected drop) - see <see cref="ServerConnectionGroup.AutoConnect"/>.
    /// </summary>
    public void AddNewGroup(string name, string host, int port, string password, string slotName, bool autoConnect, string? trackerReferenceInput = null, string? trackerId = null)
    {
        var group = new ServerConnectionGroup
        {
            Name = name,
            Host = host,
            Port = port,
            Password = password,
            AutoConnect = autoConnect,
            TrackerReferenceInput = trackerReferenceInput,
            TrackerId = trackerId
        };

        var slot = new SlotProfile { GroupId = group.Id, SlotName = slotName };
        group.Slots.Add(slot);

        if (autoConnect)
        {
            group.PreferredLeaderSlotId = slot.Id;
        }

        var groupViewModel = new GroupViewModel(group, _connectionManager, _multiworldTrackerService);
        Groups.Add(groupViewModel);
        SelectedGroup = groupViewModel;
        PersistGroups();

        _ = _connectionManager.SwitchLeaderAsync(groupViewModel, slot);
    }

    /// <summary>
    /// Adds one or more new slots to an already-existing server in one go
    /// (see <see cref="Views.ConnectionEditorWindow"/>'s search+multi-select
    /// picker) - one <see cref="PersistGroups"/> call for the whole batch
    /// rather than one per slot, and one <see cref="GroupViewModel.AddSlotsToGroup"/>
    /// call so the "Chat as" dropdown only rebuilds once regardless of how
    /// many slots this batch contains (see that method's doc comment - a
    /// plain per-slot <c>Group.Slots.Add</c> loop visibly glitched it once
    /// batches of a few dozen slots became the normal case). If the server
    /// currently has a leader, runs a brief catch-up sync for each new slot
    /// so it starts out with an up-to-date backlog (see Umsetzungsplan.md,
    /// Phase 6) - this is the one case where adding a slot does trigger
    /// network activity right away. Each <see cref="StagedSlot"/> carries
    /// its own optional per-slot password override (see
    /// <see cref="SlotProfile.Password"/>), null/empty meaning just use the
    /// group's shared password.
    /// </summary>
    public void AddSlotsToGroup(GroupViewModel groupViewModel, IReadOnlyList<StagedSlot> slotsToAdd)
    {
        if (slotsToAdd.Count == 0)
        {
            return;
        }

        var addedSlots = slotsToAdd.Select(staged => new SlotProfile
        {
            GroupId = groupViewModel.Group.Id,
            SlotName = staged.SlotName,
            Password = string.IsNullOrWhiteSpace(staged.Password) ? null : staged.Password
        }).ToList();

        groupViewModel.AddSlotsToGroup(addedSlots);

        PersistGroups();

        if (groupViewModel.LeaderSlotId is not null)
        {
            _ = CatchUpNewSlotsSequentiallyAsync(groupViewModel, addedSlots);
        }
    }

    /// <summary>
    /// Runs <see cref="IConnectionManager.CatchUpSyncAsync"/> for each of
    /// several newly-added slots one at a time rather than all at once -
    /// firing them all concurrently (each briefly opening its own session
    /// to the same room) is exactly the kind of simultaneous-handshake
    /// burst <see cref="ConnectionManager.StartupGroupSpacing"/> already
    /// avoids between different servers at startup, and an Archipelago
    /// server can rate-limit or outright reject that many connection
    /// attempts landing on it at once from several slots of the same room.
    /// </summary>
    private async Task CatchUpNewSlotsSequentiallyAsync(GroupViewModel groupViewModel, IReadOnlyList<SlotProfile> slots)
    {
        foreach (var slot in slots)
        {
            await _connectionManager.CatchUpSyncAsync(groupViewModel, slot);
        }
    }

    /// <summary>
    /// Room players not yet configured as a slot on <paramref name="groupViewModel"/>,
    /// for the "Add slot" dialog's picker (see <see cref="Views.ConnectionEditorWindow"/>).
    /// May briefly open/close a connection under the hood if the server has
    /// no live session right now - see <see cref="IConnectionManager.GetRoomPlayersAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<PlayerChoice>> GetAvailableSlotsToAddAsync(GroupViewModel groupViewModel)
    {
        var players = await _connectionManager.GetRoomPlayersAsync(groupViewModel);
        var configuredNames = new HashSet<string>(
            groupViewModel.Group.Slots.Select(s => s.SlotName),
            StringComparer.OrdinalIgnoreCase);

        return players
            .Where(p => !configuredNames.Contains(p.Name))
            .Select(p => new PlayerChoice
            {
                SlotName = p.Name,
                DisplayText = string.Equals(p.Name, p.Alias, StringComparison.Ordinal) ? p.Name : $"{p.Name} ({p.Alias})"
            })
            .ToList();
    }

    /// <summary>
    /// Edits a server's Name/Host/Port/Password/AutoConnect/default leader/
    /// Tier 2 tracker reference, and applies every slot removal staged during
    /// this same dialog session (see <see cref="ConnectionEditorViewModel.RemoveConfiguredSlot"/>/
    /// <see cref="ConnectionEditorResult.SlotsToRemove"/>) - all as one atomic
    /// "Save" action. Slot removal (including disconnecting the group first,
    /// if the removed slot was the live leader - see <see cref="RemoveSlotFromGroupCoreAsync"/>)
    /// used to happen the instant "✕" was clicked in the dialog, before Save;
    /// that could disconnect the user mid-edit purely from picking a new
    /// default leader and then removing the old one in the same session, with
    /// no chance to reconsider via Cancel. Deferring it here means Cancel now
    /// actually cancels a removal too, same as every other field in that
    /// dialog.
    /// </summary>
    public async Task UpdateGroup(
        GroupViewModel groupViewModel, string name, string host, int port, string password, bool autoConnect,
        Guid? preferredLeaderSlotId, IReadOnlyList<SlotProfile>? slotsToRemove = null,
        string? trackerReferenceInput = null, string? trackerId = null)
    {
        groupViewModel.Group.Name = name;
        groupViewModel.Group.Host = host;
        groupViewModel.Group.Port = port;
        groupViewModel.Group.Password = password;
        groupViewModel.Group.AutoConnect = autoConnect;
        groupViewModel.Group.PreferredLeaderSlotId = preferredLeaderSlotId;
        groupViewModel.Group.TrackerReferenceInput = trackerReferenceInput;
        groupViewModel.Group.TrackerId = trackerId;

        if (slotsToRemove is { Count: > 0 })
        {
            // One at a time, not concurrently - same "slots never connect/
            // disconnect concurrently" invariant as everywhere else in this
            // app, and in practice at most one of these can actually be the
            // live leader anyway (only ever one leader per group).
            foreach (var slot in slotsToRemove)
            {
                await RemoveSlotFromGroupCoreAsync(groupViewModel, slot);
            }
        }

        PersistGroups();
    }

    /// <summary>
    /// Resolves a room id into that room's tracker SUUID (Tier 2 of
    /// Feature-Plaene/Archiv/Fortschrittsanzeigen.md) - forwarded to
    /// <see cref="IMultiworldTrackerService"/> so <see cref="Views.ConnectionEditorWindow"/>
    /// (via <see cref="ConnectionEditorViewModel"/>'s callback-based design)
    /// never needs its own reference to that service.
    /// </summary>
    public Task<string?> ResolveTrackerIdAsync(string roomId) => _multiworldTrackerService.ResolveTrackerIdAsync(roomId);

    public void RenameSlot(SlotProfile slot, string newName)
    {
        slot.SlotName = newName;
        PersistGroups();
    }

    /// <summary>
    /// Removes one already-configured slot from a server immediately (as
    /// opposed to <see cref="UpdateGroup"/>'s staged, Save-time batch) -
    /// currently unused by <see cref="Views.ConnectionEditorWindow"/> itself
    /// (see <see cref="UpdateGroup"/>'s doc comment for why removal there is
    /// deferred instead), kept as a public building block for any future
    /// call site that genuinely wants an immediate removal outside that
    /// dialog's edit-then-save flow.
    /// </summary>
    public async Task RemoveSlotFromGroup(GroupViewModel groupViewModel, SlotProfile slot)
    {
        await RemoveSlotFromGroupCoreAsync(groupViewModel, slot);
        PersistGroups();
    }

    /// <summary>
    /// The actual removal logic shared by <see cref="RemoveSlotFromGroup"/>
    /// (immediate) and <see cref="UpdateGroup"/>'s staged batch - deliberately
    /// does not call <see cref="PersistGroups"/> itself, so a batch of several
    /// removals persists once at the end instead of once per slot.
    ///
    /// If the removed slot is the current leader, disconnects it first
    /// (<see cref="IConnectionManager.DisconnectGroupAsync"/> also clears
    /// <see cref="GroupViewModel.LeaderSlotId"/>/<see cref="GroupViewModel.SelectedChatSlot"/>
    /// through its normal guarded path) rather than pulling its
    /// configuration out from under a live session. The three independent
    /// slot filters and the group's own default-leader preference are reset
    /// too if they were pointing at this slot, so nothing is left dangling.
    /// Removal itself goes through <see cref="GroupViewModel.RemoveSlotFromGroup"/>
    /// (not a raw <c>Group.Slots.Remove</c>) - see that method's doc comment
    /// for why: a raw removal used to make the *leader* silently vanish from
    /// the "Chat as" dropdown whenever some other, unrelated slot was removed.
    /// </summary>
    private async Task RemoveSlotFromGroupCoreAsync(GroupViewModel groupViewModel, SlotProfile slot)
    {
        if (groupViewModel.LeaderSlotId == slot.Id)
        {
            await _connectionManager.DisconnectGroupAsync(groupViewModel);
        }

        if (groupViewModel.SelectedEventsSlotFilter == slot)
        {
            groupViewModel.SelectedEventsSlotFilter = null;
        }

        if (groupViewModel.SelectedHintsSlotFilter == slot)
        {
            groupViewModel.SelectedHintsSlotFilter = null;
        }

        if (groupViewModel.SelectedItemsSlotFilter == slot)
        {
            groupViewModel.SelectedItemsSlotFilter = null;
        }

        if (groupViewModel.Group.PreferredLeaderSlotId == slot.Id)
        {
            groupViewModel.Group.PreferredLeaderSlotId = null;
        }

        groupViewModel.RemoveSlotFromGroup(slot);
    }

    /// <summary>
    /// Every currently configured server; used by the connection editor to
    /// detect duplicate Host+Port (when creating a new server) or duplicate
    /// slot names within a server (when adding/renaming a slot).
    /// </summary>
    public IReadOnlyList<ServerConnectionGroup> GetAllGroups() => Groups.Select(g => g.Group).ToList();

    public AppSettings LoadSettings() => _persistenceService.LoadSettings();

    public void SaveSettings(AppSettings settings) => _persistenceService.SaveSettings(settings);

    /// <summary>
    /// Disconnects every server that currently has a leader; each disconnect
    /// sticks (no AutoConnect bring-back) until the user reconnects that
    /// server manually, same as the per-server Disconnect button.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAllGroupsAsync()
    {
        var groupsToDisconnect = Groups.Where(g => g.LeaderSlotId is not null).ToList();
        foreach (var groupViewModel in groupsToDisconnect)
        {
            await _connectionManager.DisconnectGroupAsync(groupViewModel);
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var groupToRemove = SelectedGroup;

        if (groupToRemove.LeaderSlotId is not null)
        {
            await _connectionManager.DisconnectGroupAsync(groupToRemove);
        }

        var index = Groups.IndexOf(groupToRemove);
        Groups.Remove(groupToRemove);

        SelectedGroup = Groups.Count > 0 ? Groups[Math.Min(index, Groups.Count - 1)] : null;

        // Feature-Plaene/Archiv/Hint-Eingabefeld.md's per-group DataPackage
        // cache has nothing left to serve once this server is gone - clean it
        // up rather than leaving an orphaned cache folder behind forever.
        _persistenceService.DeleteDataPackageCacheForGroup(groupToRemove.Group.Id);

        PersistGroups();
    }

    private void PersistGroups() => _persistenceService.SaveGroups(GetAllGroups());
}
