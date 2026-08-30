using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One row in the "Add slot" dialog's checkbox list - wraps a
/// <see cref="PlayerChoice"/> with the per-row UI state the search +
/// multi-select design needs (see Umsetzungsplan.md, "Add slot"-Dialog:
/// Mehrfachauswahl mit Suche - replacing the one-at-a-time ComboBox + "Add
/// to list" flow). <see cref="ShowOverride"/>/<see cref="OverridePassword"/>
/// are the rare-case escape hatch for a slot on a custom-hosted room that
/// needs its own password - most rows never touch them, falling back to the
/// group's own shared password instead (see
/// <see cref="ViewModels.ConnectionEditorViewModel.BuildSlotsToAdd"/>).
/// </summary>
public partial class SelectableSlotRow : ObservableObject
{
    public required PlayerChoice Player { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether this row's own password-override field is expanded - collapsed by default since per-slot overrides are rare.</summary>
    [ObservableProperty]
    private bool _showOverride;

    /// <summary>This row's own password override, or null/empty to fall back to the group's shared password (see <see cref="ViewModels.ConnectionEditorViewModel.BuildSlotsToAdd"/>).</summary>
    [ObservableProperty]
    private string? _overridePassword;
}
