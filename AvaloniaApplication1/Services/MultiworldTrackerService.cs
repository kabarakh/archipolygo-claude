using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Real <see cref="IMultiworldTrackerService"/> - talks to an Archipelago
/// webhost's own public JSON API (verified against <c>docs/webhost api.md</c>
/// in the ArchipelagoMW/Archipelago repository), a completely separate REST
/// endpoint from the <c>Archipelago.MultiClient.Net</c> socket connection
/// <see cref="ConnectionManager"/> uses everywhere else - the room server
/// (Host:Port) this app already stores and the webhost (archipelago.gg, or a
/// self-hosted instance of it) are two different things, and a client logged
/// into the room socket has no way to discover the tracker id on its own
/// (see Feature-Plaene/Archiv/Fortschrittsanzeigen.md for the full writeup).
///
/// Strictly honors the documented cache timers - <c>/tracker/...</c> at most
/// once every 60 seconds, <c>/static_tracker/...</c> at most once every 300
/// seconds - per tracker id, regardless of how often <see cref="GetProgressAsync"/>
/// itself is called; a cached response is reused instead of refetching.
/// </summary>
public class MultiworldTrackerService : IMultiworldTrackerService
{
    private static readonly TimeSpan TrackerCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaticTrackerCacheDuration = TimeSpan.FromSeconds(300);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _clock;

    private readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, TrackerResponse Data)> _trackerCache = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, StaticTrackerResponse Data)> _staticTrackerCache = new();

    /// <param name="httpClient">
    /// Defaults to a fresh client against archipelago.gg - override for a
    /// self-hosted webhost instance, or (in tests) to inject a fake
    /// <see cref="HttpMessageHandler"/> so JSON-parsing tests never touch the
    /// real network (see Test-Umsetzungsplan.md's ban on real network timing).
    /// </param>
    /// <param name="clock">Overridable for tests that need to assert the cache timers are actually honored, without a real wait.</param>
    public MultiworldTrackerService(HttpClient? httpClient = null, Func<DateTimeOffset>? clock = null)
    {
        _httpClient = httpClient ?? new HttpClient { BaseAddress = new Uri("https://archipelago.gg/") };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string?> ResolveTrackerIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RoomStatusResponse>(
                $"api/room_status/{Uri.EscapeDataString(roomId)}", JsonOptions, cancellationToken);
            return string.IsNullOrEmpty(response?.Tracker) ? null : response.Tracker;
        }
        catch (Exception)
        {
            // Room not found, no webhost at all, network error, unexpected
            // JSON shape - all treated the same: no tracker available. See
            // the interface doc comment for why this never throws.
            return null;
        }
    }

    public async Task<RoomProgressSnapshot?> GetProgressAsync(string trackerId, CancellationToken cancellationToken = default)
    {
        var tracker = await GetTrackerAsync(trackerId, cancellationToken);
        if (tracker is null)
        {
            return null;
        }

        // The static tracker (location totals) is best-effort on top of the
        // live tracker data - a room whose static data hasn't loaded yet (or
        // failed to) still gets checked-count progress, just without a
        // "Y" denominator (ChecksTotal stays null - see PlayerProgress).
        var staticTracker = await GetStaticTrackerAsync(trackerId, cancellationToken);

        var totalsByPlayer = (staticTracker?.PlayerLocationsTotal ?? new())
            .ToDictionary(p => (p.Team, p.Player), p => p.TotalLocations);
        var gamesByPlayer = (staticTracker?.PlayerGame ?? new())
            .ToDictionary(p => (p.Team, p.Player), p => p.Game);
        var aliasesByPlayer = tracker.Aliases
            .ToDictionary(a => (a.Team, a.Player), a => a.Alias);

        var players = tracker.PlayerChecksDone
            .Select(entry => new PlayerProgress
            {
                Team = entry.Team,
                Player = entry.Player,
                Alias = aliasesByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                Game = gamesByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                ChecksDone = entry.Locations.Count,
                ChecksTotal = totalsByPlayer.TryGetValue((entry.Team, entry.Player), out var total) ? total : null,
            })
            .ToList();

        return new RoomProgressSnapshot { Players = players };
    }

    private async Task<TrackerResponse?> GetTrackerAsync(string trackerId, CancellationToken cancellationToken)
    {
        if (_trackerCache.TryGetValue(trackerId, out var cached) && _clock() - cached.FetchedAt < TrackerCacheDuration)
        {
            return cached.Data;
        }

        try
        {
            var data = await _httpClient.GetFromJsonAsync<TrackerResponse>(
                $"api/tracker/{Uri.EscapeDataString(trackerId)}", JsonOptions, cancellationToken);
            if (data is null)
            {
                return null;
            }

            _trackerCache[trackerId] = (_clock(), data);
            return data;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<StaticTrackerResponse?> GetStaticTrackerAsync(string trackerId, CancellationToken cancellationToken)
    {
        if (_staticTrackerCache.TryGetValue(trackerId, out var cached) && _clock() - cached.FetchedAt < StaticTrackerCacheDuration)
        {
            return cached.Data;
        }

        try
        {
            var data = await _httpClient.GetFromJsonAsync<StaticTrackerResponse>(
                $"api/static_tracker/{Uri.EscapeDataString(trackerId)}", JsonOptions, cancellationToken);
            if (data is null)
            {
                return null;
            }

            _staticTrackerCache[trackerId] = (_clock(), data);
            return data;
        }
        catch (Exception)
        {
            // No cached static data at all yet - GetProgressAsync's caller
            // just gets ChecksTotal == null for every player this round.
            return null;
        }
    }

    // Response DTOs below use JsonNamingPolicy.SnakeCaseLower (see JsonOptions)
    // to match the webhost API's snake_case JSON keys without needing a
    // [JsonPropertyName] on every single property - "PlayerChecksDone" maps to
    // "player_checks_done", etc. Only the fields this service actually reads
    // are modeled; every other documented key is intentionally left out.

    private sealed class RoomStatusResponse
    {
        public string? Tracker { get; set; }
    }

    private sealed class TrackerResponse
    {
        public List<AliasEntry> Aliases { get; set; } = new();
        public List<PlayerChecksDoneEntry> PlayerChecksDone { get; set; } = new();
    }

    private sealed class AliasEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public string? Alias { get; set; }
    }

    private sealed class PlayerChecksDoneEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public List<long> Locations { get; set; } = new();
    }

    private sealed class StaticTrackerResponse
    {
        public List<PlayerLocationsTotalEntry> PlayerLocationsTotal { get; set; } = new();
        public List<PlayerGameEntry> PlayerGame { get; set; } = new();
    }

    private sealed class PlayerLocationsTotalEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public int TotalLocations { get; set; }
    }

    private sealed class PlayerGameEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public string? Game { get; set; }
    }
}
