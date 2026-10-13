using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// Hands out enqueued sessions in order (each connect attempt, incl. retries, gets a new one), else a fresh default.
/// </summary>
public sealed class FakeSessionFactory : ISessionFactory
{
    private readonly Queue<FakeArchipelagoSession> _queuedSessions = new();

    public List<FakeArchipelagoSession> CreatedSessions { get; } = new();

    public Dictionary<IArchipelagoSession, FakeDeathLinkService> DeathLinkServicesBySession { get; } = new();

    public void Enqueue(FakeArchipelagoSession session) => _queuedSessions.Enqueue(session);

    public IArchipelagoSession CreateSession(string host, int port)
    {
        var session = _queuedSessions.Count > 0 ? _queuedSessions.Dequeue() : new FakeArchipelagoSession();
        CreatedSessions.Add(session);
        return session;
    }

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
