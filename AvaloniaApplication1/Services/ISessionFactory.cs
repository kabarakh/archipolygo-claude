using Archipelago.MultiClient.Net;

namespace Archipolygo.Services;

/// <summary>
/// The one seam between <see cref="ConnectionManager"/> and the concrete
/// <c>Archipelago.MultiClient.Net</c> library - everywhere else,
/// <see cref="ConnectionManager"/> talks only to <see cref="IArchipelagoSession"/>
/// (which <see cref="ArchipelagoSession"/> already implements, no wrapper
/// needed there - see <see cref="ArchipelagoSessionFactoryAdapter"/> for the
/// one static call this replaces). Exists purely so
/// AvaloniaApplication1.Tests can substitute a
/// <c>FakeArchipelagoSession</c>/<c>FakeSessionFactory</c> to exercise
/// <see cref="ConnectionManager"/>'s real locking/ordering logic (Kategorie B,
/// see Test-Umsetzungsplan.md) with fully controllable, instant "connects" -
/// no other behavior change for the real app.
/// </summary>
public interface ISessionFactory
{
    IArchipelagoSession CreateSession(string host, int port);
}
