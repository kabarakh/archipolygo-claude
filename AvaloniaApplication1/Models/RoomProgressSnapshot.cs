using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// One player's progress in a room's webhost tracker (Tier 2 of
/// Fortschrittsanzeigen.md) - combines a "/tracker/&lt;id&gt;" response's
/// per-player checked-location count with a "/static_tracker/&lt;id&gt;"
/// response's per-player location total, keyed by (<see cref="Team"/>,
/// <see cref="Player"/>) - see <see cref="Services.MultiworldTrackerService"/>.
/// </summary>
public sealed class PlayerProgress
{
    public required int Team { get; init; }

    public required int Player { get; init; }

    /// <summary>Player alias from the tracker's own "aliases" list; null if the player never set one, in which case the room's own slot name (not available here) is a better display fallback.</summary>
    public string? Alias { get; init; }

    /// <summary>The game this player is playing, from the static tracker's "player_game" list - null if not (yet) known.</summary>
    public string? Game { get; init; }

    public required int ChecksDone { get; init; }

    /// <summary>Null until the static tracker response (the source of location totals) has been fetched at least once.</summary>
    public int? ChecksTotal { get; init; }

    /// <summary>Alias if set, else a generic "Player N" fallback - the tracker API only ever gives numeric player ids and (optionally) an alias, never the actual configured slot name.</summary>
    public string DisplayName => string.IsNullOrEmpty(Alias) ? $"Player {Player}" : Alias!;

    /// <summary><see cref="ChecksDone"/> as a plain <c>double</c>, for binding directly to a <c>ProgressBar.Value</c> without a converter.</summary>
    public double ChecksDoneValue => ChecksDone;

    /// <summary><see cref="ChecksTotal"/> as a plain <c>double</c> (defaulting to 0 while unknown), for binding directly to a <c>ProgressBar.Maximum</c> without a converter.</summary>
    public double ChecksTotalValue => ChecksTotal ?? 0;

    public string ChecksText => ChecksTotal is not null ? $"{ChecksDone}/{ChecksTotal}" : ChecksDone.ToString();
}

/// <summary>
/// One combined snapshot of a room's whole-multiworld progress - see
/// <see cref="Services.IMultiworldTrackerService.GetProgressAsync"/>.
/// </summary>
public sealed class RoomProgressSnapshot
{
    public required IReadOnlyList<PlayerProgress> Players { get; init; }
}
