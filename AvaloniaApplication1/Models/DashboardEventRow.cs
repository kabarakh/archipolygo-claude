using Archipolygo.ViewModels;

namespace Archipolygo.Models;

/// <summary>
/// One row of the Dashboard's shared, server-spanning Events view (see
/// Feature-Plaene/Archiv/Dashboard-Tab.md's follow-up "geteilte
/// Event-Ansicht"). <see cref="EventEntry"/> only carries a
/// <see cref="EventEntry.SlotId"/>, not a reference to the group it belongs
/// to, so this small wrapper ties the two together - same reasoning as
/// <see cref="DashboardHintRow"/>.
/// </summary>
public sealed record DashboardEventRow(GroupViewModel Group, EventEntry Event);
