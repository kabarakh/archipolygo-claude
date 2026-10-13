using Archipolygo.ViewModels;

namespace Archipolygo.Models;

/// <summary>A Dashboard Hints row; ties a <see cref="HintEntry"/> (which only has a SlotId) to its group.</summary>
public sealed record DashboardHintRow(GroupViewModel Group, HintEntry Hint);
