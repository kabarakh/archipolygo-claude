using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.ViewModels;

/// <summary>Applying a config import. See Config-Verwaltung.md, "Import".</summary>
public partial class MainWindowViewModel
{
    /// <summary>Optional (null in tests): the Settings dialog then hides its data section.</summary>
    private readonly DataDirectory? _dataDirectory;

    private readonly IConfigExportService? _configExportService;

    public DataManagementContext? CreateDataManagementContext() =>
        _dataDirectory is null || _configExportService is null
            ? null
            : new DataManagementContext(_dataDirectory, _configExportService, GetAllGroups, LoadSettings, StartConfigImport);

    private void StartConfigImport(ConfigImportSelection selection) => _ = ApplyConfigImportLoggingFailuresAsync(selection);

    private async Task ApplyConfigImportLoggingFailuresAsync(ConfigImportSelection selection)
    {
        try
        {
            await ApplyConfigImportAsync(selection);
        }
        catch (Exception ex)
        {
            _diagnosticLogger.Error("Applying a config import failed", ex);
        }
    }

    /// <summary>
    /// Completes only after every reconnect, so callers that don't want to wait should not await it.
    /// Overwritten groups are updated in place, so their tab, position and detached window survive.
    /// </summary>
    public async Task ApplyConfigImportAsync(ConfigImportSelection selection)
    {
        var plan = ConfigImportMerger.Merge(GetAllGroups(), selection.Groups);
        var groupsToConnect = new List<GroupViewModel>();

        foreach (var overwrite in plan.Overwrites)
        {
            var groupViewModel = Groups.First(g => g.Group == overwrite.Local);
            var wasConnected = groupViewModel.LeaderSlotId is not null;
            if (wasConnected)
            {
                await _connectionManager.DisconnectGroupAsync(groupViewModel);
            }

            OverwriteGroupInPlace(groupViewModel, overwrite.Replacement);

            if (wasConnected || overwrite.Replacement.AutoConnect)
            {
                groupsToConnect.Add(groupViewModel);
            }
        }

        foreach (var group in plan.NewGroups)
        {
            if (string.IsNullOrEmpty(group.Color))
            {
                group.Color = ServerColorPalette.AssignColor(Groups.Select(g => g.Group.Color));
            }

            var groupViewModel = CreateGroupViewModel(group);
            Groups.Add(groupViewModel);
            DockedGroups.Add(groupViewModel);

            if (group.AutoConnect)
            {
                groupsToConnect.Add(groupViewModel);
            }
        }

        foreach (var slotId in plan.SlotIdsWithObsoleteSyncState)
        {
            _persistenceService.DeleteSyncState(slotId);
        }

        PersistGroups();

        if (selection.Settings is { } settings)
        {
            ApplyImportedSettings(settings);
        }

        await ConnectGroupsSequentiallyAsync(groupsToConnect);
    }

    private void OverwriteGroupInPlace(GroupViewModel groupViewModel, ServerConnectionGroup replacement)
    {
        var group = groupViewModel.Group;
        group.Name = replacement.Name;
        group.Host = replacement.Host;
        group.Port = replacement.Port;
        group.AutoConnect = replacement.AutoConnect;
        group.PreferredLeaderSlotId = replacement.PreferredLeaderSlotId;
        group.IsAdmin = replacement.IsAdmin;
        group.TrackerReferenceInput = replacement.TrackerReferenceInput;
        group.TrackerId = replacement.TrackerId;
        ApplyNotificationsMuted(groupViewModel, replacement.NotificationsMuted);

        if (!string.IsNullOrEmpty(replacement.Color))
        {
            group.Color = replacement.Color;
        }

        var replacementSlotIds = replacement.Slots.Select(s => s.Id).ToHashSet();
        foreach (var droppedSlot in group.Slots.Where(s => !replacementSlotIds.Contains(s.Id)).ToList())
        {
            groupViewModel.RemoveSlotFromGroup(droppedSlot);
        }

        var addedSlots = new List<SlotProfile>();
        foreach (var importedSlot in replacement.Slots)
        {
            if (group.Slots.FirstOrDefault(s => s.Id == importedSlot.Id) is { } keptSlot)
            {
                keptSlot.SlotName = importedSlot.SlotName;
                keptSlot.Alias = importedSlot.Alias;
                keptSlot.RequiresPassword = importedSlot.RequiresPassword;
            }
            else
            {
                addedSlots.Add(importedSlot);
            }
        }

        groupViewModel.AddSlotsToGroup(addedSlots);
    }

    /// <summary>Same as the Settings dialog's Save, plus what only the toolbar or startup applies otherwise.</summary>
    private void ApplyImportedSettings(AppSettings settings)
    {
        SaveSettings(settings);

        ThemePreference = settings.ThemePreference;
        ThemeService.Apply(settings.ThemePreference);

        FilterLayout.EventsFiltersExpanded = settings.EventsFiltersExpanded;
        FilterLayout.RightPanelFiltersExpanded = settings.RightPanelFiltersExpanded;
        FilterLayout.DashboardEventsFiltersExpanded = settings.DashboardEventsFiltersExpanded;
        FilterLayout.DashboardHintsFiltersExpanded = settings.DashboardHintsFiltersExpanded;
    }

    /// <summary>Spaced like startup, so importing several servers doesn't fire a burst of logins.</summary>
    private async Task ConnectGroupsSequentiallyAsync(IReadOnlyList<GroupViewModel> groups)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Group.ResolveStartupLeader() is { } leader)
            {
                await _connectionManager.SwitchLeaderAsync(groups[i], leader);
            }

            if (i < groups.Count - 1)
            {
                await Task.Delay(ConnectionManager.StartupGroupSpacing);
            }
        }
    }
}
