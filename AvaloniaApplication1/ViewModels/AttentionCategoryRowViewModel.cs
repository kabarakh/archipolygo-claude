using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>One row of the Settings dialog's "Notifications" table - the two independent toggles of an <see cref="AttentionCategorySetting"/>, see the feature-plan archive's <c>Benachrichtigungen.md</c>.</summary>
public partial class AttentionCategoryRowViewModel : ObservableObject
{
    public required AttentionCategory Category { get; init; }

    public required string Label { get; init; }

    [ObservableProperty]
    private bool _count;

    [ObservableProperty]
    private bool _blink;
}
