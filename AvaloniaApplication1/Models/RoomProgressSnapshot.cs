using System;
using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// One player's progress from the webhost tracker: checked count from "/tracker/&lt;id&gt;",
/// location total from "/static_tracker/&lt;id&gt;", joined on (<see cref="Team"/>, <see cref="Player"/>).
/// </summary>
public sealed class PlayerProgress
{
    public required int Team { get; init; }

    public required int Player { get; init; }

    /// <summary>Null if the player never set an alias.</summary>
    public string? Alias { get; init; }

    /// <summary>From the static tracker's "player_game"; null if not (yet) known.</summary>
    public string? Game { get; init; }

    public required int ChecksDone { get; init; }

    /// <summary>Null until the static tracker response has been fetched at least once.</summary>
    public int? ChecksTotal { get; init; }

    /// <summary>The tracker API never exposes slot names, only numeric ids and optional aliases.</summary>
    public string DisplayName => string.IsNullOrEmpty(Alias) ? $"Player {Player}" : Alias!;

    public double ChecksDoneValue => ChecksDone;

    public double ChecksTotalValue => ChecksTotal ?? 0;

    public string ChecksText => ChecksTotal is not null ? $"{ChecksDone}/{ChecksTotal}" : ChecksDone.ToString();

    /// <summary>"activity_timers": when this player last checked a NEW location; null if never connected.</summary>
    public DateTimeOffset? LastActivity { get; init; }

    /// <summary>"player_status": an Archipelago ClientStatus (0 unknown, 5 connected, 10 ready, 20 playing, 30 goal); null if not reported.</summary>
    public int? ClientStatus { get; init; }

    /// <summary>"player_checks_done" location ids, so Admin "Send location" can hide checked ones for any player.</summary>
    public IReadOnlyList<long> CheckedLocationIds { get; init; } = Array.Empty<long>();
}

public sealed class RoomProgressSnapshot
{
    public required IReadOnlyList<PlayerProgress> Players { get; init; }
}
