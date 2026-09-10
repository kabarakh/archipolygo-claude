using System;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>
/// Fake <see cref="IDeathLinkService"/> for Kategorie B (Test-Umsetzungsplan.md,
/// Feature-Plaene/Archiv/DeathLink.md) - <see cref="RaiseDeathLinkReceived"/>
/// lets a test simulate an incoming DeathLink, and <see cref="IsEnabled"/>
/// records whether <see cref="ConnectionManager"/> actually called
/// <see cref="EnableDeathLink"/> (it always should, for the leader session -
/// see <c>ConnectionManagerDeathLinkTests</c>) without ever touching a real
/// connection's tags.
/// </summary>
public sealed class FakeDeathLinkService : IDeathLinkService
{
    public event Action<DeathLink>? OnDeathLinkReceived;

    public bool IsEnabled { get; private set; }

    public void EnableDeathLink() => IsEnabled = true;

    /// <summary>Simulates the server delivering a DeathLink bounce packet to this session.</summary>
    public void RaiseDeathLinkReceived(DeathLink deathLink) => OnDeathLinkReceived?.Invoke(deathLink);
}
