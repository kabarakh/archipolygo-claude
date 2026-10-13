using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>One row in the "Add slot" dialog's searchable multi-select list.</summary>
public partial class SelectableSlotRow : ObservableObject
{
    public required PlayerChoice Player { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Collapsed by default since per-slot password overrides are rare.</summary>
    [ObservableProperty]
    private bool _showOverride;

    /// <summary>Null/empty falls back to the group's shared password.</summary>
    [ObservableProperty]
    private string? _overridePassword;
}
