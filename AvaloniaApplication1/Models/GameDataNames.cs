using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// One game's item and location names from the room's DataPackage - see
/// <see cref="Services.IConnectionManager.GetGameDataAsync"/>. Used by the
/// Admin view's player dialog, which needs these for ANY player's game, not
/// just one of this app's own slots.
/// </summary>
public sealed class GameDataNames
{
    public required IReadOnlyList<string> ItemNames { get; init; }

    /// <summary>Location name -> id, so already checked location ids from the tracker can be filtered out.</summary>
    public required IReadOnlyDictionary<string, long> LocationIds { get; init; }
}
