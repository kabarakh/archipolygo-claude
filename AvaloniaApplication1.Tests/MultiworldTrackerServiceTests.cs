using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md -
/// <see cref="MultiworldTrackerService"/>'s JSON parsing and cache-timer
/// throttling, against fixed embedded fixture responses (the exact examples
/// from ArchipelagoMW/Archipelago's <c>docs/webhost api.md</c>) via a fake
/// <see cref="HttpMessageHandler"/> - never a real network call, same
/// principle as Test-Umsetzungsplan.md's ban on real network/timing in tests.
/// </summary>
public class MultiworldTrackerServiceTests
{
    private const string RoomStatusJson = """
        {
            "tracker": "2gVkMQgISGScA8wsvDZg5A",
            "players": [["Slot_Name_1", "Ocarina of Time"]],
            "last_port": 52122,
            "last_activity": "Fri, 18 Apr 2025 20:35:45 GMT",
            "timeout": 7200,
            "downloads": []
        }
        """;

    private const string TrackerJson = """
        {
          "aliases": [
            { "team": 0, "player": 1, "alias": "Incompetence" },
            { "team": 0, "player": 2, "alias": null }
          ],
          "player_items_received": [],
          "player_checks_done": [
            { "team": 0, "player": 1, "locations": [1, 2] },
            { "team": 0, "player": 2, "locations": [1] }
          ],
          "total_checks_done": [ { "team": 0, "checks_done": 3 } ],
          "hints": [],
          "activity_timers": [],
          "connection_timers": [],
          "player_status": []
        }
        """;

    private const string StaticTrackerJson = """
        {
          "groups": [],
          "datapackage": {},
          "player_locations_total": [
            { "player": 1, "team": 0, "total_locations": 10 },
            { "player": 2, "team": 0, "total_locations": 20 }
          ],
          "player_game": [
            { "team": 0, "player": 1, "game": "Archipelago" },
            { "team": 0, "player": 2, "game": "The Messenger" }
          ]
        }
        """;

    /// <summary>Routes by URL substring to a fixed canned response - records every request it served, so a test can assert how many times each endpoint was actually hit (for the cache-timer tests).</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responsesByUrlSubstring;
        public List<string> RequestedUrls { get; } = new();

        public FakeHandler(Dictionary<string, string> responsesByUrlSubstring) => _responsesByUrlSubstring = responsesByUrlSubstring;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            foreach (var (substring, json) in _responsesByUrlSubstring)
            {
                if (url.Contains(substring))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static MultiworldTrackerService MakeService(
        Dictionary<string, string> responses, out FakeHandler handler, Func<DateTimeOffset>? clock = null)
    {
        handler = new FakeHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://archipelago.gg/") };
        return new MultiworldTrackerService(httpClient, clock);
    }

    [Fact]
    public async Task ResolveTrackerIdAsync_ParsesTrackerFieldFromRoomStatus()
    {
        var service = MakeService(new() { ["room_status"] = RoomStatusJson }, out _);

        var trackerId = await service.ResolveTrackerIdAsync("kK5fmxd8TfisU5Yp_eg");

        Assert.Equal("2gVkMQgISGScA8wsvDZg5A", trackerId);
    }

    [Fact]
    public async Task ResolveTrackerIdAsync_NotFound_ReturnsNullRatherThanThrowing()
    {
        var service = MakeService(new(), out _); // no route matches -> 404

        var trackerId = await service.ResolveTrackerIdAsync("does-not-exist");

        Assert.Null(trackerId);
    }

    [Fact]
    public async Task GetProgressAsync_CombinesTrackerAndStaticTracker_IntoPerPlayerChecksDoneOverTotal()
    {
        var service = MakeService(
            new() { ["/api/tracker/"] = TrackerJson, ["static_tracker"] = StaticTrackerJson },
            out _);

        var snapshot = await service.GetProgressAsync("2gVkMQgISGScA8wsvDZg5A");

        Assert.NotNull(snapshot);
        var player1 = snapshot!.Players.Single(p => p.Player == 1);
        Assert.Equal("Incompetence", player1.Alias);
        Assert.Equal("Archipelago", player1.Game);
        Assert.Equal(2, player1.ChecksDone); // len(player_checks_done[1].locations)
        Assert.Equal(10, player1.ChecksTotal);

        var player2 = snapshot.Players.Single(p => p.Player == 2);
        Assert.Null(player2.Alias); // "alias": null in the fixture
        Assert.Equal(1, player2.ChecksDone);
        Assert.Equal(20, player2.ChecksTotal);
    }

    [Fact]
    public async Task GetProgressAsync_StaticTrackerUnavailable_StillReturnsCheckedCounts_WithNullTotals()
    {
        // Only /tracker/ responds - static_tracker 404s, e.g. a transient failure.
        var service = MakeService(new() { ["/api/tracker/"] = TrackerJson }, out _);

        var snapshot = await service.GetProgressAsync("2gVkMQgISGScA8wsvDZg5A");

        Assert.NotNull(snapshot);
        Assert.All(snapshot!.Players, p => Assert.Null(p.ChecksTotal));
        Assert.Equal(2, snapshot.Players.Single(p => p.Player == 1).ChecksDone);
    }

    [Fact]
    public async Task GetProgressAsync_TrackerUnavailable_ReturnsNull()
    {
        var service = MakeService(new(), out _); // everything 404s

        var snapshot = await service.GetProgressAsync("does-not-exist");

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetProgressAsync_WithinCacheWindow_DoesNotRefetchEitherEndpoint()
    {
        var now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = MakeService(
            new() { ["/api/tracker/"] = TrackerJson, ["static_tracker"] = StaticTrackerJson },
            out var handler,
            clock: () => now);

        await service.GetProgressAsync("tracker-id");
        Assert.Equal(2, handler.RequestedUrls.Count); // one tracker + one static_tracker call

        now = now.AddSeconds(30); // well within both the 60s and 300s cache windows
        await service.GetProgressAsync("tracker-id");

        Assert.Equal(2, handler.RequestedUrls.Count); // both served from cache - no new requests
    }

    [Fact]
    public async Task GetProgressAsync_PastTrackerCacheWindow_RefetchesTrackerButNotStaticTracker()
    {
        var now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = MakeService(
            new() { ["/api/tracker/"] = TrackerJson, ["static_tracker"] = StaticTrackerJson },
            out var handler,
            clock: () => now);

        await service.GetProgressAsync("tracker-id");
        Assert.Equal(2, handler.RequestedUrls.Count);

        now = now.AddSeconds(61); // past the 60s tracker cache, still within the 300s static one
        await service.GetProgressAsync("tracker-id");

        Assert.Equal(3, handler.RequestedUrls.Count); // one more /tracker/ call, static_tracker still cached
        Assert.Equal(2, handler.RequestedUrls.Count(u => u.Contains("/api/tracker/")));
        Assert.Equal(1, handler.RequestedUrls.Count(u => u.Contains("static_tracker")));
    }
}
