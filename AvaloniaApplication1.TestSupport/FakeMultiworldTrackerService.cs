using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>Returns pre-arranged snapshots and records every call (to check polling frequency).</summary>
public sealed class FakeMultiworldTrackerService : IMultiworldTrackerService
{
    private readonly Dictionary<string, string?> _trackerIdsByRoomId = new();
    private readonly Dictionary<string, RoomProgressSnapshot?> _progressByTrackerId = new();
    private readonly Dictionary<string, RoomConnectionInfo?> _connectionInfoByRoomId = new();

    public List<string> ResolveTrackerIdCalls { get; } = new();
    public List<string> GetProgressCalls { get; } = new();
    public List<string> ResolveRoomConnectionInfoCalls { get; } = new();

    public void SetTrackerIdForRoom(string roomId, string? trackerId) => _trackerIdsByRoomId[roomId] = trackerId;

    public void SetProgress(string trackerId, RoomProgressSnapshot? snapshot) => _progressByTrackerId[trackerId] = snapshot;

    private readonly Dictionary<(string TrackerId, int Team, int Player), IReadOnlyList<TrackerReceivedItem>?> _receivedItems = new();

    public List<(string TrackerId, int Team, int Player)> GetReceivedItemsCalls { get; } = new();

    public void SetReceivedItems(string trackerId, int team, int player, IReadOnlyList<TrackerReceivedItem>? items) =>
        _receivedItems[(trackerId, team, player)] = items;

    public Task<IReadOnlyList<TrackerReceivedItem>?> GetReceivedItemsAsync(string trackerId, int team, int player, CancellationToken cancellationToken = default)
    {
        GetReceivedItemsCalls.Add((trackerId, team, player));
        return Task.FromResult(_receivedItems.GetValueOrDefault((trackerId, team, player)));
    }

    public void SetConnectionInfoForRoom(string roomId, RoomConnectionInfo? info) => _connectionInfoByRoomId[roomId] = info;

    public Task<string?> ResolveTrackerIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        ResolveTrackerIdCalls.Add(roomId);
        return Task.FromResult(_trackerIdsByRoomId.GetValueOrDefault(roomId));
    }

    public Task<RoomProgressSnapshot?> GetProgressAsync(string trackerId, CancellationToken cancellationToken = default)
    {
        GetProgressCalls.Add(trackerId);
        return Task.FromResult(_progressByTrackerId.GetValueOrDefault(trackerId));
    }

    public Task<RoomConnectionInfo?> ResolveRoomConnectionInfoAsync(string roomId, CancellationToken cancellationToken = default)
    {
        ResolveRoomConnectionInfoCalls.Add(roomId);
        return Task.FromResult(_connectionInfoByRoomId.GetValueOrDefault(roomId));
    }
}
