using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipolygo.Services;


namespace Archipolygo.TestSupport;

/// <summary>
/// Fake <see cref="ISessionFactory"/> for Kategorie B (Test-Umsetzungsplan.md).
/// Hands out pre-scripted <see cref="FakeArchipelagoSession"/> instances in
/// the order <see cref="Enqueue"/> was called, so a test can control exactly
/// which session a particular connect *attempt* gets - important for e.g.
/// <c>ConnectSlotSessionAsync</c>'s retry loop, which calls
/// <see cref="CreateSession"/> again (a brand-new session) on every attempt.
/// If nothing was enqueued, hands out a fresh default
/// <see cref="FakeArchipelagoSession"/> instead, for tests that don't care
/// which exact instance they get - see <see cref="CreatedSessions"/> to fetch
/// whatever was actually created.
/// </summary>
public sealed class FakeSessionFactory : ISessionFactory
{
    private readonly Queue<FakeArchipelagoSession> _queuedSessions = new();

    /// <summary>Every session actually handed out so far, in creation order.</summary>
    public List<FakeArchipelagoSession> CreatedSessions { get; } = new();

    /// <summary>
    /// Every <see cref="FakeDeathLinkService"/> handed out by <see cref="CreateDeathLinkService"/>
    /// so far, keyed by the session it was created for - lets a test fetch
    /// "the DeathLink service <see cref="Archipolygo.Services.ConnectionManager"/>
    /// created for this leader session" to call <see cref="FakeDeathLinkService.RaiseDeathLinkReceived"/>
    /// on, or check <see cref="FakeDeathLinkService.IsEnabled"/>.
    /// </summary>
    public Dictionary<IArchipelagoSession, FakeDeathLinkService> DeathLinkServicesBySession { get; } = new();

    public void Enqueue(FakeArchipelagoSession session) => _queuedSessions.Enqueue(session);

    public IArchipelagoSession CreateSession(string host, int port)
    {
        var session = _queuedSessions.Count > 0 ? _queuedSessions.Dequeue() : new FakeArchipelagoSession();
        CreatedSessions.Add(session);
        return session;
    }

    /// <summary>One <see cref="FakeDeathLinkService"/> per session, created lazily on first request - see <see cref="DeathLinkServicesBySession"/>.</summary>
    public IDeathLinkService CreateDeathLinkService(IArchipelagoSession session)
    {
        if (!DeathLinkServicesBySession.TryGetValue(session, out var deathLinkService))
        {
            deathLinkService = new FakeDeathLinkService();
            DeathLinkServicesBySession[session] = deathLinkService;
        }

        return deathLinkService;
    }
}
