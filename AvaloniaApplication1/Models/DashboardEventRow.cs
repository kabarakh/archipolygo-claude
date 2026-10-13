using Archipolygo.ViewModels;

namespace Archipolygo.Models;

/// <summary>A Dashboard Events row; ties an <see cref="EventEntry"/> (which only has a SlotId) to its group.</summary>
public sealed record DashboardEventRow(GroupViewModel Group, EventEntry Event);
