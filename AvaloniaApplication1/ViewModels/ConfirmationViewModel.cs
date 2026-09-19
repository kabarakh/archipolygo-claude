namespace Archipolygo.ViewModels;

/// <summary>
/// Backs <see cref="Views.ConfirmationWindow"/> - the app's one generic "are
/// you sure?" dialog, reused for every destructive action that can't be
/// cleanly undone (removing a server, removing a configured slot from a
/// server) rather than each call site growing its own bespoke confirmation
/// window. Built fresh per confirmation, same as <see cref="PasswordPromptViewModel"/>.
/// </summary>
public sealed class ConfirmationViewModel : ViewModelBase
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "Remove";
}
