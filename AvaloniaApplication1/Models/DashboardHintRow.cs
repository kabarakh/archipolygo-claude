using Archipolygo.ViewModels;

namespace Archipolygo.Models;

/// <summary>
/// One row of the Dashboard's shared, server-spanning Hints list (see
/// Feature-Plaene/Archiv/Dashboard-Tab.md). <see cref="HintEntry"/> only carries a
/// <see cref="HintEntry.SlotId"/>, not a reference to the group it belongs
/// to, so this small wrapper ties the two together - analogous to the
/// existing dialog-only helper records (<c>StagedSlot</c>/<c>ConfiguredSlotRow</c>/
/// <c>PlayerChoice</c>).
/// </summary>
public sealed record DashboardHintRow(GroupViewModel Group, HintEntry Hint);
