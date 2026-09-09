using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Tier 2 of Fortschrittsanzeigen.md: whole-multiworld progress (every
/// player in the room, not just this app's own configured slots), sourced
/// from the room's Archipelago webhost tracker API - a completely separate
/// HTTP endpoint from the <c>Archipelago.MultiClient.Net</c> socket
/// connection used everywhere else in this app (see
/// <see cref="MultiworldTrackerService"/>'s own doc comment for why). Only
/// works for a room hosted via a webhost (archipelago.gg or a self-hosted
/// instance) - a no-op for a custom-hosted <c>MultiServer.py</c> room with no
/// webhost component, since those have no tracker at all.
/// </summary>
public interface IMultiworldTrackerService
{
    /// <summary>
    /// Resolves a room id (extracted from a room URL by
    /// <see cref="TrackerReferenceParser"/>) into that room's tracker SUUID,
    /// via <c>/room_status/&lt;room_id&gt;</c>. Null on any failure (room not
    /// found, no webhost, network error) - never throws.
    /// </summary>
    Task<string?> ResolveTrackerIdAsync(string roomId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every player's current "X of Y locations checked" progress for the
    /// room behind <paramref name="trackerId"/>, combining a
    /// <c>/tracker/&lt;id&gt;</c> call (checked counts; cached for at most 60
    /// seconds - never fetched more often than that) with a
    /// <c>/static_tracker/&lt;id&gt;</c> call (location totals; cached for at
    /// most 300 seconds, since this barely ever changes within a room's
    /// lifetime). Null on any failure - never throws; callers should treat
    /// that the same as "no data available right now" rather than an error
    /// that needs to interrupt anything.
    /// </summary>
    Task<RoomProgressSnapshot?> GetProgressAsync(string trackerId, CancellationToken cancellationToken = default);
}
