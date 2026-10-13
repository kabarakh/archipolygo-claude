using System;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>Raises DeathLinks on demand and records EnableDeathLink without touching real connection tags.</summary>
public sealed class FakeDeathLinkService : IDeathLinkService
{
    public event Action<DeathLink>? OnDeathLinkReceived;

    public bool IsEnabled { get; private set; }

    public void EnableDeathLink() => IsEnabled = true;

    public void RaiseDeathLinkReceived(DeathLink deathLink) => OnDeathLinkReceived?.Invoke(deathLink);
}
