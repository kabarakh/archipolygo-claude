using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One server's section within the password-prompt dialog (see
/// Feature-Plaene/Passwort-Speicherung.md and its mockup
/// <c>passwort-dialog-mockup.html</c>): the group's own shared-password
/// fallback field, plus one field per slot that specifically needs its own
/// password. <see cref="Slots"/> is never empty - a group only appears here
/// at all because at least one of its slots currently needs a password (see
/// <see cref="ViewModels.MainWindowViewModel.HandlePasswordRequestedAsync"/>).
/// </summary>
public partial class PasswordPromptGroupRow : ObservableObject
{
    public required ServerConnectionGroup Group { get; init; }

    public required IReadOnlyList<PasswordPromptSlotRow> Slots { get; init; }

    /// <summary>
    /// Whether there's an actual group-vs-slot distinction worth a toggle at
    /// all - only when 2+ slots on this server need one. A single-slot group
    /// just shows its one (group-level) field with nothing to expand,
    /// matching the mockup's "Server ABC"/"Server C" examples.
    /// </summary>
    public bool ShowSlotToggle => Slots.Count > 1;

    /// <summary>
    /// Whether the per-slot fields are currently shown, collapsed behind a
    /// 🔒 toggle button by default - same convention as
    /// <see cref="SelectableSlotRow.ShowOverride"/> in the "Add slot"
    /// dialog. Collapsed by default per dev feedback: the overwhelming
    /// majority of rooms share one password across every slot, so a
    /// multi-slot server would otherwise show one redundant field per slot
    /// even though filling in just the group's own field above already
    /// satisfies all of them. <see cref="ViewModels.MainWindowViewModel.BuildPasswordPromptViewModel"/>
    /// initializes this to already-expanded when any of <see cref="Slots"/>
    /// has <see cref="PasswordPromptSlotRow.ShowError"/> set - a retry round
    /// must never hide the one field the user actually needs to see and fix
    /// behind a collapsed toggle.
    /// </summary>
    [ObservableProperty]
    private bool _slotFieldsExpanded;

    /// <summary>Computes the correct initial <see cref="SlotFieldsExpanded"/> for a freshly built set of rows - see that property's own doc comment.</summary>
    public static bool ShouldStartExpanded(IReadOnlyList<PasswordPromptSlotRow> slots) => slots.Any(s => s.ShowError);
}
