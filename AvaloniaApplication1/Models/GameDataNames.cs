using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>One game's item and location names from the DataPackage, for any player's game (Admin player dialog).</summary>
public sealed class GameDataNames
{
    public required IReadOnlyList<string> ItemNames { get; init; }

    /// <summary>Location name -> id, so already checked location ids from the tracker can be filtered out.</summary>
    public required IReadOnlyDictionary<string, long> LocationIds { get; init; }
}
