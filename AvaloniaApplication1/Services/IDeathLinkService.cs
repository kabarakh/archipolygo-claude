using System;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace Archipolygo.Services;

/// <summary>
/// Own, minimal wrapper around <c>Archipelago.MultiClient.Net.BounceFeatures.DeathLink.DeathLinkService</c>
/// (see Feature-Plaene/Archiv/DeathLink.md) - exists purely because the real
/// <c>DeathLinkService</c> can only be created via
/// <c>DeathLinkProvider.CreateDeathLinkService(this ArchipelagoSession)</c>, an
/// extension method on the concrete <c>ArchipelagoSession</c> type, not on
/// <see cref="Archipelago.MultiClient.Net.IArchipelagoSession"/> - see <see cref="ISessionFactory.CreateDeathLinkService"/>
/// for the seam this plugs into, same reasoning as <see cref="ISessionFactory"/>
/// itself. Deliberately has no <c>SendDeathLink</c>/<c>DisableDeathLink</c>:
/// this app never sends a DeathLink, only surfaces incoming ones (see the
/// plan doc's "kein Sende-Button" rationale), and always enables reception
/// for the leader session with nothing to ever turn it back off again (see
/// <see cref="Archipolygo.Services.ConnectionManager"/>'s doc comment at its
/// call site for why there's no per-group toggle) - only what's actually used
/// lives here, same principle as every other fake/wrapper in this codebase.
/// </summary>
public interface IDeathLinkService
{
    event Action<DeathLink>? OnDeathLinkReceived;

    void EnableDeathLink();
}
