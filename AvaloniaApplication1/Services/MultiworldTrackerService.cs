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
/// Webhost JSON API client. Strictly honors the documented cache timers per tracker id
/// (<c>/tracker</c> 60s, <c>/static_tracker</c> 300s), however often it's called.
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

    /// <param name="httpClient">Override for a self-hosted webhost, or a fake handler in tests.</param>
    /// <param name="clock">For tests asserting the cache timers without real waits.</param>
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
            // Not found, no webhost, network error, bad JSON: all mean "no tracker".
            return null;
        }
    }

    public async Task<RoomConnectionInfo?> ResolveRoomConnectionInfoAsync(string roomId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<RoomStatusResponse>(
                $"api/room_status/{Uri.EscapeDataString(roomId)}", JsonOptions, cancellationToken);

            if (response is null || response.LastPort is null or <= 0)
            {
                return null;
            }

            // room_status has no hostname field; webhost rooms run on the webhost's own domain.
            var host = _httpClient.BaseAddress?.Host;
            if (string.IsNullOrEmpty(host))
            {
                return null;
            }

            return new RoomConnectionInfo
            {
                Host = host,
                Port = response.LastPort.Value,
                TrackerId = string.IsNullOrEmpty(response.Tracker) ? null : response.Tracker
            };
        }
        catch (Exception)
        {
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

        // Static totals are best-effort: without them ChecksTotal just stays null.
        var staticTracker = await GetStaticTrackerAsync(trackerId, cancellationToken);

        var totalsByPlayer = (staticTracker?.PlayerLocationsTotal ?? new())
            .ToDictionary(p => (p.Team, p.Player), p => p.TotalLocations);
        var gamesByPlayer = (staticTracker?.PlayerGame ?? new())
            .ToDictionary(p => (p.Team, p.Player), p => p.Game);
        var aliasesByPlayer = tracker.Aliases
            .ToDictionary(a => (a.Team, a.Player), a => a.Alias);

        var activityByPlayer = tracker.ActivityTimers
            .GroupBy(t => (t.Team, t.Player))
            .ToDictionary(g => g.Key, g => ParseTimestamp(g.First().Time));
        var statusByPlayer = tracker.PlayerStatus
            .GroupBy(s => (s.Team, s.Player))
            .ToDictionary(g => g.Key, g => g.First().Status);

        var players = tracker.PlayerChecksDone
            .Select(entry => new PlayerProgress
            {
                Team = entry.Team,
                Player = entry.Player,
                Alias = aliasesByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                Game = gamesByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                ChecksDone = entry.Locations.Count,
                ChecksTotal = totalsByPlayer.TryGetValue((entry.Team, entry.Player), out var total) ? total : null,
                LastActivity = activityByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                ClientStatus = statusByPlayer.GetValueOrDefault((entry.Team, entry.Player)),
                CheckedLocationIds = entry.Locations,
            })
            .ToList();

        return new RoomProgressSnapshot { Players = players };
    }

    public async Task<IReadOnlyList<TrackerReceivedItem>?> GetReceivedItemsAsync(string trackerId, int team, int player,
                                                                                CancellationToken cancellationToken = default)
    {
        var tracker = await GetTrackerAsync(trackerId, cancellationToken);
        var entry = tracker?.PlayerItemsReceived.FirstOrDefault(e => e.Team == team && e.Player == player);
        if (entry is null)
        {
            return null;
        }

        var items = new List<TrackerReceivedItem>(entry.Items.Count);
        foreach (var element in entry.Items)
        {
            if (!TryParseNetworkItem(element, out var item))
            {
                return null;
            }

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// NetworkItem is a Python NamedTuple (item, location, player, flags), which Flask serializes as an array;
    /// the object form is accepted too in case a webhost serializes it by field name.
    /// </summary>
    internal static bool TryParseNetworkItem(JsonElement element, out TrackerReceivedItem item)
    {
        item = null!;
        long itemId, locationId;
        int sender, flags = 0;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var length = element.GetArrayLength();
            if (length < 3 ||
                !TryGetLong(element[0], out itemId) ||
                !TryGetLong(element[1], out locationId) ||
                !TryGetInt(element[2], out sender) ||
                (length > 3 && !TryGetInt(element[3], out flags)))
            {
                return false;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (!element.TryGetProperty("item", out var itemProperty) || !TryGetLong(itemProperty, out itemId) ||
                !element.TryGetProperty("location", out var locationProperty) || !TryGetLong(locationProperty, out locationId) ||
                !element.TryGetProperty("player", out var playerProperty) || !TryGetInt(playerProperty, out sender))
            {
                return false;
            }

            if (element.TryGetProperty("flags", out var flagsProperty) && !TryGetInt(flagsProperty, out flags))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        item = new TrackerReceivedItem(itemId, locationId, sender, flags);
        return true;
    }

    // JsonElement.TryGetInt64 throws instead of returning false for a non-number.
    private static bool TryGetLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
    }

    private static bool TryGetInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
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
            return null;
        }
    }

    // DTOs rely on SnakeCaseLower naming; only fields this service reads are modeled.

    private sealed class RoomStatusResponse
    {
        public string? Tracker { get; set; }

        /// <summary>"Last known hosted port" - the room's actual port.</summary>
        public int? LastPort { get; set; }
    }

    /// <summary>RFC 1123 (Flask's default), or null for a player who never connected; unreadable means unknown.</summary>
    internal static DateTimeOffset? ParseTimestamp(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private sealed class TrackerResponse
    {
        public List<AliasEntry> Aliases { get; set; } = new();
        public List<PlayerChecksDoneEntry> PlayerChecksDone { get; set; } = new();
        public List<PlayerTimerEntry> ActivityTimers { get; set; } = new();
        public List<PlayerStatusEntry> PlayerStatus { get; set; } = new();
        public List<PlayerItemsReceivedEntry> PlayerItemsReceived { get; set; } = new();
    }

    private sealed class PlayerItemsReceivedEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public List<JsonElement> Items { get; set; } = new();
    }

    private sealed class PlayerTimerEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public string? Time { get; set; }
    }

    private sealed class PlayerStatusEntry
    {
        public int Team { get; set; }
        public int Player { get; set; }
        public int? Status { get; set; }
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
