using System;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

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

    /// <summary>
    /// <see cref="DeathLinkProvider.CreateDeathLinkService"/> is an extension
    /// method on the concrete <see cref="ArchipelagoSession"/> type, not
    /// <see cref="IArchipelagoSession"/> (see Feature-Plaene/Archiv/DeathLink.md) -
    /// the cast below should always succeed in the real app, since
    /// <see cref="CreateSession"/> only ever hands out real
    /// <see cref="ArchipelagoSession"/> instances; null here just means "no
    /// DeathLink support for this session" rather than throwing.
    /// </summary>
    public IDeathLinkService? CreateDeathLinkService(IArchipelagoSession session) =>
        session is ArchipelagoSession realSession
            ? new ArchipelagoDeathLinkServiceAdapter(realSession.CreateDeathLinkService())
            : null;
}

/// <summary>
/// Wraps the real <see cref="DeathLinkService"/> behind <see cref="IDeathLinkService"/> -
/// just re-raises its <see cref="DeathLinkService.OnDeathLinkReceived"/> event
/// (a differently-typed delegate, so it can't be assigned directly to an
/// <see cref="Action{DeathLink}"/>-typed event) and forwards
/// <see cref="EnableDeathLink"/> verbatim.
/// </summary>
internal sealed class ArchipelagoDeathLinkServiceAdapter : IDeathLinkService
{
    private readonly DeathLinkService _inner;

    public ArchipelagoDeathLinkServiceAdapter(DeathLinkService inner)
    {
        _inner = inner;
        _inner.OnDeathLinkReceived += deathLink => OnDeathLinkReceived?.Invoke(deathLink);
    }

    public event Action<DeathLink>? OnDeathLinkReceived;

    public void EnableDeathLink() => _inner.EnableDeathLink();
}
