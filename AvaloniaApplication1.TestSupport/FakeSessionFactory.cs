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

    public void Enqueue(FakeArchipelagoSession session) => _queuedSessions.Enqueue(session);

    public IArchipelagoSession CreateSession(string host, int port)
    {
        var session = _queuedSessions.Count > 0 ? _queuedSessions.Dequeue() : new FakeArchipelagoSession();
        CreatedSessions.Add(session);
        return session;
    }
}
