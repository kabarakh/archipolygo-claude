using System;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace Archipolygo.Services;

/// <summary>
/// Test seam: the real <c>DeathLinkService</c> can only be created from the concrete <c>ArchipelagoSession</c>.
/// No send/disable members: the app only ever receives DeathLinks.
/// </summary>
public interface IDeathLinkService
{
    event Action<DeathLink>? OnDeathLinkReceived;

    void EnableDeathLink();
}
