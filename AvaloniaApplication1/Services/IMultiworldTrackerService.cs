using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Whole-room progress via the webhost tracker HTTP API (separate from the socket connection).
/// No-op for bare <c>MultiServer.py</c> rooms, which have no tracker.
/// </summary>
public interface IMultiworldTrackerService
{
    /// <summary>Via <c>/room_status/&lt;room_id&gt;</c>. Null on any failure; never throws.</summary>
    Task<string?> ResolveTrackerIdAsync(string roomId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checked counts cached at most 60s, location totals 300s, to avoid hammering the webhost.
    /// Null on any failure (treat as "no data right now"); never throws.
    /// </summary>
    Task<RoomProgressSnapshot?> GetProgressAsync(string trackerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Host/Port (+ tracker id) from one <c>/room_status</c> call. Host is always this service's own
    /// webhost domain - <c>room_status</c> has no hostname field. Null on any failure; never throws.
    /// </summary>
    Task<RoomConnectionInfo?> ResolveRoomConnectionInfoAsync(string roomId, CancellationToken cancellationToken = default);
}
