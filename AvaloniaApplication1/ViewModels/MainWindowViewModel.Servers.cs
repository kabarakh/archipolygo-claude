using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>Adding, editing and removing servers and their slots.</summary>
public partial class MainWindowViewModel
{
    /// <summary>
    /// Creates a server with its first slot and connects it as leader right away.
    /// <paramref name="autoConnect"/> only affects reconnecting at the next app start.
    /// </summary>
    /// <param name="color">Null/empty falls back to <see cref="ServerColorPalette.AssignColor"/>.</param>
    public void AddNewGroup(string name, string host, int port, string password, string slotName, bool autoConnect, string? color = null, string? trackerReferenceInput = null, string? trackerId = null,
        bool isAdmin = false)
    {
        var group = new ServerConnectionGroup
        {
            IsAdmin = isAdmin,
            Name = name,
            Host = host,
            Port = port,
            Password = password,
            AutoConnect = autoConnect,
            TrackerReferenceInput = trackerReferenceInput,
            TrackerId = trackerId,
            Color = string.IsNullOrEmpty(color) ? ServerColorPalette.AssignColor(Groups.Select(g => g.Group.Color)) : color
        };

        var slot = new SlotProfile { GroupId = group.Id, SlotName = slotName };
        group.Slots.Add(slot);

        if (autoConnect)
        {
            group.PreferredLeaderSlotId = slot.Id;
        }

        var groupViewModel = CreateGroupViewModel(group);
        Groups.Add(groupViewModel);
        DockedGroups.Add(groupViewModel);
        SelectedGroup = groupViewModel;
        PersistGroups();

        _ = _connectionManager.SwitchLeaderAsync(groupViewModel, slot);
    }

    /// <summary>
    /// Adds a batch of slots in one go, so the "Chat as" dropdown rebuilds once. If the server
    /// is connected, each new slot gets a catch-up sync for its backlog.
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
    /// One at a time, never concurrently: a burst of simultaneous handshakes to the same room
    /// can get rate-limited or rejected by the server.
    /// </summary>
    private async Task CatchUpNewSlotsSequentiallyAsync(GroupViewModel groupViewModel, IReadOnlyList<SlotProfile> slots)
    {
        foreach (var slot in slots)
        {
            await _connectionManager.CatchUpSyncAsync(groupViewModel, slot);
        }
    }

    /// <summary>
    /// Room players not yet configured as slots. May briefly connect if the server has no live
    /// session.
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
    /// Applies the Edit dialog on Save, including slot removals staged there - deferred to
    /// Save (not done on "✕") so Cancel cancels a removal too.
    /// </summary>
    public async Task UpdateGroup(
        GroupViewModel groupViewModel, string name, string host, int port, string password, bool autoConnect,
        Guid? preferredLeaderSlotId, string? color = null, IReadOnlyList<SlotProfile>? slotsToRemove = null,
        string? trackerReferenceInput = null, string? trackerId = null, bool? notificationsMuted = null, bool? isAdmin = null)
    {
        groupViewModel.Group.Name = name;
        groupViewModel.Group.Host = host;
        groupViewModel.Group.Port = port;
        groupViewModel.Group.Password = password;
        groupViewModel.Group.AutoConnect = autoConnect;
        groupViewModel.Group.PreferredLeaderSlotId = preferredLeaderSlotId;
        groupViewModel.Group.TrackerReferenceInput = trackerReferenceInput;
        groupViewModel.Group.TrackerId = trackerId;

        // Null = leave untouched (same for isAdmin and color below).
        if (notificationsMuted is { } muted)
        {
            ApplyNotificationsMuted(groupViewModel, muted);
        }

        if (isAdmin is { } admin)
        {
            groupViewModel.Group.IsAdmin = admin;
        }

        if (!string.IsNullOrEmpty(color))
        {
            groupViewModel.Group.Color = color;
        }

        if (slotsToRemove is { Count: > 0 })
        {
            // Sequential: slots never connect/disconnect concurrently.
            foreach (var slot in slotsToRemove)
            {
                await RemoveSlotFromGroupCoreAsync(groupViewModel, slot);
            }
        }

        PersistGroups();
    }

    /// <summary>Context-menu shortcut for Edit server's mute checkbox.</summary>
    public void ToggleNotificationsMuted(GroupViewModel groupViewModel)
    {
        ApplyNotificationsMuted(groupViewModel, !groupViewModel.Group.NotificationsMuted);
        PersistGroups();
    }

    /// <summary>Muting also clears the existing unread count.</summary>
    private static void ApplyNotificationsMuted(GroupViewModel groupViewModel, bool muted)
    {
        groupViewModel.Group.NotificationsMuted = muted;
        if (muted)
        {
            groupViewModel.UnreadAttentionCount = 0;
        }
    }

    /// <summary>Forwarded so the connection editor needs no reference to the tracker service.</summary>
    public Task<string?> ResolveTrackerIdAsync(string roomId) => _multiworldTrackerService.ResolveTrackerIdAsync(roomId);

    /// <summary>Room id to Host/Port, for "paste a room link" in the connection editor.</summary>
    public Task<RoomConnectionInfo?> ResolveRoomConnectionInfoAsync(string roomId) => _multiworldTrackerService.ResolveRoomConnectionInfoAsync(roomId);

    /// <summary>
    /// Doesn't persist itself, so a batch persists once. Disconnects first if the slot is the
    /// live leader, rather than pulling its configuration out from under a session.
    /// </summary>
    private async Task RemoveSlotFromGroupCoreAsync(GroupViewModel groupViewModel, SlotProfile slot)
    {
        if (groupViewModel.LeaderSlotId == slot.Id)
        {
            await _connectionManager.DisconnectGroupAsync(groupViewModel);
        }

        if (groupViewModel.Group.PreferredLeaderSlotId == slot.Id)
        {
            groupViewModel.Group.PreferredLeaderSlotId = null;
        }

        groupViewModel.RemoveSlotFromGroup(slot);
    }

    /// <summary>Each disconnect sticks (no AutoConnect) until reconnected manually.</summary>
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
    private Task RemoveSelectedGroupAsync() => SelectedGroup is null ? Task.CompletedTask : RemoveGroupAsync(SelectedGroup);

    /// <summary>
    /// Removes a server after confirmation (expensive to undo). Only reselects if the removed
    /// group was the selected one.
    /// </summary>
    public async Task RemoveGroupAsync(GroupViewModel groupToRemove)
    {
        if (ShowConfirmationDialogAsync is not null)
        {
            var confirmed = await ShowConfirmationDialogAsync(new ConfirmationViewModel
            {
                Title = "Remove server?",
                Message = $"Remove \"{groupToRemove.Group.Name}\" and every slot configured on it? " +
                          "You'll need to reconnect and re-sync from scratch if you add it back later.",
            });

            if (!confirmed)
            {
                return;
            }
        }

        if (groupToRemove.LeaderSlotId is not null)
        {
            await _connectionManager.DisconnectGroupAsync(groupToRemove);
        }

        // DockedGroups index, not Groups: SelectedGroup is always a docked group,
        // so its replacement must come from there too.
        var dockedIndex = DockedGroups.IndexOf(groupToRemove);
        Groups.Remove(groupToRemove);
        DockedGroups.Remove(groupToRemove);

        if (groupToRemove.IsDetached)
        {
            CloseDetachedWindow?.Invoke(groupToRemove);
        }

        if (SelectedGroup == groupToRemove)
        {
            SelectedGroup = DockedGroups.Count > 0
                ? DockedGroups[Math.Min(Math.Max(dockedIndex, 0), DockedGroups.Count - 1)]
                : null;
        }

        _persistenceService.DeleteDataPackageCacheForGroup(groupToRemove.Group.Id);

        PersistGroups();
    }
}
