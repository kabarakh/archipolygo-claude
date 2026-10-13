namespace Archipolygo.ViewModels;

/// <summary>The generic "are you sure?" dialog for destructive actions; built fresh per confirmation.</summary>
public sealed class ConfirmationViewModel : ViewModelBase
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "Remove";
}
