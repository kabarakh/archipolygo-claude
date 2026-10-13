using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One server's section in the password-prompt dialog: the group's shared-password field plus one field per slot
/// that needs a password. <see cref="Slots"/> is never empty.
/// </summary>
public partial class PasswordPromptGroupRow : ObservableObject
{
    public required ServerConnectionGroup Group { get; init; }

    public required IReadOnlyList<PasswordPromptSlotRow> Slots { get; init; }

    /// <summary>Only with 2+ slots is there a group-vs-slot distinction worth a toggle.</summary>
    public bool ShowSlotToggle => Slots.Count > 1;

    /// <summary>
    /// Collapsed by default since most rooms share one password, but must start expanded when any row
    /// shows an error, so a retry never hides the field the user needs to fix.
    /// </summary>
    [ObservableProperty]
    private bool _slotFieldsExpanded;

    public static bool ShouldStartExpanded(IReadOnlyList<PasswordPromptSlotRow> slots) => slots.Any(s => s.ShowError);
}
