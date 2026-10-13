using Archipelago.MultiClient.Net;

namespace Archipolygo.Services;

/// <summary>
/// The one seam to <c>Archipelago.MultiClient.Net</c>, so tests can substitute a fake session
/// factory to exercise <see cref="ConnectionManager"/>'s real locking logic.
/// </summary>
public interface ISessionFactory
{
    IArchipelagoSession CreateSession(string host, int port);

    /// <summary>
    /// Needs an already logged-in session. Null if <paramref name="session"/> isn't a real
    /// <see cref="ArchipelagoSession"/> (never in the real app).
    /// </summary>
    IDeathLinkService? CreateDeathLinkService(IArchipelagoSession session);
}
