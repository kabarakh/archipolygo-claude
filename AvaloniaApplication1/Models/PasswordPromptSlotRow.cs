using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One slot's password field in a <see cref="PasswordPromptGroupRow"/>. Binds straight to the in-memory
/// <see cref="SlotProfile.Password"/>, so a waiting connect attempt sees it with no separate apply step.
/// </summary>
public partial class PasswordPromptSlotRow : ObservableObject
{
    public required SlotProfile Slot { get; init; }

    /// <summary>True for exactly the one slot a wrong-password retry round is about.</summary>
    [ObservableProperty]
    private bool _showError;
}
