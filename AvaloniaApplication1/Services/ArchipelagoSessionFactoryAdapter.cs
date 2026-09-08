using Archipelago.MultiClient.Net;

namespace Archipolygo.Services;

/// <summary>
/// Production <see cref="ISessionFactory"/> - just forwards to the library's
/// own static <see cref="ArchipelagoSessionFactory.CreateSession"/>. See
/// <see cref="ISessionFactory"/>'s doc comment for why this indirection
/// exists at all.
/// </summary>
public class ArchipelagoSessionFactoryAdapter : ISessionFactory
{
    public IArchipelagoSession CreateSession(string host, int port) =>
        ArchipelagoSessionFactory.CreateSession(host, port);
}
