using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;

namespace Archipolygo.TestSupport;

/// <summary>Only <see cref="GetClientStatusAsync"/> has behavior: it answers from <see cref="ClientStatuses"/>.</summary>
public sealed class FakeDataStorageHelper : IDataStorageHelper
{
    public Dictionary<int, ArchipelagoClientState> ClientStatuses { get; } = new();

    /// <summary>When set, every status read fails with it.</summary>
    public Exception? ClientStatusFailure { get; set; }

    public List<int> RequestedClientStatusSlots { get; } = new();

    public Task<ArchipelagoClientState> GetClientStatusAsync(int? slot = null, int? team = null)
    {
        RequestedClientStatusSlots.Add(slot ?? 0);
        return ClientStatusFailure is not null
            ? Task.FromException<ArchipelagoClientState>(ClientStatusFailure)
            : Task.FromResult(ClientStatuses.GetValueOrDefault(slot ?? 0, ArchipelagoClientState.ClientUnknown));
    }

    public DataStorageElement this[Scope scope, string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public DataStorageElement this[string key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public ArchipelagoClientState GetClientStatus(int? slot = null, int? team = null) => throw new NotImplementedException();
    public void TrackClientStatus(Action<ArchipelagoClientState> onStatusUpdated, bool retrieveCurrentClientStatus = true, int? slot = null, int? team = null) => throw new NotImplementedException();
    public Hint[] GetHints(int? slot = null, int? team = null) => throw new NotImplementedException();
    public Task<Hint[]> GetHintsAsync(int? slot = null, int? team = null) => throw new NotImplementedException();
    public void TrackHints(Action<Hint[]> onHintsUpdated, bool retrieveCurrentlyUnlockedHints = true, int? slot = null, int? team = null) => throw new NotImplementedException();
    public Dictionary<string, object> GetSlotData(int? slot = null) => throw new NotImplementedException();
    public T GetSlotData<T>(int? slot = null) where T : class => throw new NotImplementedException();
    public Task<Dictionary<string, object>> GetSlotDataAsync(int? slot = null) => throw new NotImplementedException();
    public Task<T> GetSlotDataAsync<T>(int? slot = null) where T : class => throw new NotImplementedException();
    public Dictionary<string, string[]> GetItemNameGroups(string? game = null) => throw new NotImplementedException();
    public Task<Dictionary<string, string[]>> GetItemNameGroupsAsync(string? game = null) => throw new NotImplementedException();
    public Dictionary<string, string[]> GetLocationNameGroups(string? game = null) => throw new NotImplementedException();
    public Task<Dictionary<string, string[]>> GetLocationNameGroupsAsync(string? game = null) => throw new NotImplementedException();
    public bool GetRaceMode() => throw new NotImplementedException();
    public Task<bool> GetRaceModeAsync() => throw new NotImplementedException();
}

/// <summary>Only <see cref="Seed"/> has behavior.</summary>
public sealed class FakeRoomStateHelper : IRoomStateHelper
{
    public string Seed { get; set; } = "seed-1";

    public int HintCost => throw new NotImplementedException();
    public int HintCostPercentage => throw new NotImplementedException();
    public int LocationCheckPoints => throw new NotImplementedException();
    public int HintPoints => throw new NotImplementedException();
    public Version Version => throw new NotImplementedException();
    public Version GeneratorVersion => throw new NotImplementedException();
    public bool HasPassword => throw new NotImplementedException();
    public Permissions ReleasePermissions => throw new NotImplementedException();
    public Permissions ForfeitPermissions => throw new NotImplementedException();
    public Permissions CollectPermissions => throw new NotImplementedException();
    public Permissions RemainingPermissions => throw new NotImplementedException();
    public DateTime RoomInfoSendTime => throw new NotImplementedException();
    public ReadOnlyCollection<string> ServerTags => throw new NotImplementedException();
}
