using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One slot's own password field within a <see cref="PasswordPromptGroupRow"/> -
/// see Feature-Plaene/Passwort-Speicherung.md. Binds straight through to
/// <see cref="SlotProfile.Password"/> (an in-memory-only property, see its
/// own doc comment) rather than staging a separate value, so typing here
/// takes effect immediately for whatever connect attempt is waiting on it -
/// no separate "apply" step needed once the dialog closes.
/// </summary>
public partial class PasswordPromptSlotRow : ObservableObject
{
    public required SlotProfile Slot { get; init; }

    /// <summary>
    /// True for exactly the one slot a retry round is about (see
    /// <see cref="Services.IConnectionManager.PasswordRequested"/>'s
    /// <c>isRetryAfterFailure</c>) - highlights this row with an inline
    /// "Wrong password" message rather than the plain field every other row
    /// gets.
    /// </summary>
    [ObservableProperty]
    private bool _showError;
}
