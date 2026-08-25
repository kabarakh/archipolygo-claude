using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.ViewModels;

namespace TestHarness;

/// <summary>
/// No-network stand-in for <see cref="IConnectionManager"/> - see
/// .claude/skills/app-testen. Never opens a real Archipelago session;
/// "connecting" a slot just flips the view model's own state so the UI reads
/// as connected. Test code adds synthetic <see cref="EventEntry"/>/
/// <see cref="HintEntry"/>/<see cref="ReceivedItemEntry"/> directly to a
/// GroupViewModel's collections rather than going through this class.
/// </summary>
public sealed class FakeConnectionManager : IConnectionManager
{
    public event Action<GroupViewModel>? GroupPersistNeeded;
    public event Action<GroupViewModel, SlotProfile>? SlotInitialSyncCompleted;

    public Task SwitchLeaderAsync(GroupViewModel group, SlotProfile targetSlot)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(targetSlot.Id, targetSlot);
        group.ConnectionState = ConnectionState.Connected;
        GroupPersistNeeded?.Invoke(group);
        return Task.CompletedTask;
    }

    public Task DisconnectGroupAsync(GroupViewModel group)
    {
        group.SetLeaderStateWithoutTriggeringSwitch(null, null);
        group.ConnectionState = ConnectionState.Disconnected;
        GroupPersistNeeded?.Invoke(group);
        return Task.CompletedTask;
    }

    public Task CatchUpSyncAsync(GroupViewModel group, SlotProfile slot) => Task.CompletedTask;

    public Task SendMessageAsync(GroupViewModel group, string text) => Task.CompletedTask;

    public Task InitializeGroupAsync(GroupViewModel group)
    {
        // Immediately mark every configured slot as "processed" so
        // MainWindowViewModel's startup-sync banner doesn't get stuck at
        // "Catching up slots: 0/N" forever - nothing here ever fires it.
        foreach (var slot in group.Group.Slots.ToList())
        {
            SlotInitialSyncCompleted?.Invoke(group, slot);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlayerInfo>> GetRoomPlayersAsync(GroupViewModel group) =>
        Task.FromResult<IReadOnlyList<PlayerInfo>>(Array.Empty<PlayerInfo>());
}
