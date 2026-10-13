using System;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace Archipolygo.Services;

/// <summary>Production <see cref="ISessionFactory"/>, forwarding to the library's static factory.</summary>
public class ArchipelagoSessionFactoryAdapter : ISessionFactory
{
    public IArchipelagoSession CreateSession(string host, int port) =>
        ArchipelagoSessionFactory.CreateSession(host, port);

    /// <summary>
    /// <see cref="DeathLinkProvider.CreateDeathLinkService"/> only extends the concrete
    /// <see cref="ArchipelagoSession"/>; returns null (no DeathLink) for anything else.
    /// </summary>
    public IDeathLinkService? CreateDeathLinkService(IArchipelagoSession session) =>
        session is ArchipelagoSession realSession
            ? new ArchipelagoDeathLinkServiceAdapter(realSession.CreateDeathLinkService())
            : null;
}

/// <summary>
/// Re-raises <see cref="DeathLinkService.OnDeathLinkReceived"/>, whose delegate type
/// can't be assigned to an <see cref="Action{DeathLink}"/> event directly.
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
