using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// No-network stand-in for <see cref="IMultiworldTrackerService"/> - Tier 2 of
/// Feature-Plaene/Archiv/Fortschrittsanzeigen.md. A test arranges whatever
/// <see cref="RoomProgressSnapshot"/>/tracker id it wants returned for a given
/// key up front; every call is also recorded so a test can assert
/// <see cref="GroupViewModel.RefreshMultiworldProgressCommand"/> actually
/// reached this service (and how often - relevant for the "don't poll more
/// than once per tab-open" behavior).
/// </summary>
public sealed class FakeMultiworldTrackerService : IMultiworldTrackerService
{
    private readonly Dictionary<string, string?> _trackerIdsByRoomId = new();
    private readonly Dictionary<string, RoomProgressSnapshot?> _progressByTrackerId = new();

    public List<string> ResolveTrackerIdCalls { get; } = new();
    public List<string> GetProgressCalls { get; } = new();

    public void SetTrackerIdForRoom(string roomId, string? trackerId) => _trackerIdsByRoomId[roomId] = trackerId;

    public void SetProgress(string trackerId, RoomProgressSnapshot? snapshot) => _progressByTrackerId[trackerId] = snapshot;

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
}
