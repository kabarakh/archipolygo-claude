using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TestHarness.AddSlotPickerPrototype;

/// <summary>
/// Prototype for the redesigned "Add slot" picker - see Umsetzungsplan.md
/// ("Add slot"-Dialog: Mehrfachauswahl mit Suche) for the full reasoning.
/// Combines ideas from Archipolygo's current ComboBox + staging-list flow
/// with `multi_slot_tracker_webui`'s SlotPicker.vue (search filter over a
/// checkbox list, "select/deselect visible"). Unlike that Vue picker (which
/// only ever *watches* slots rather than logging into them), rows here keep
/// the existing per-slot password override for the rare custom-hosted slot
/// that needs its own password - the group's shared password itself is
/// out of scope here, since it's already entered when the group's leader
/// first connects, before any slot picking happens.
///
/// Deliberately NOT wired into
/// <see cref="Archipolygo.ViewModels.MainWindowViewModel.AddSlotsToGroup"/>
/// yet - this is a click-through prototype for design feedback (see
/// .claude/skills/ui-feature-prototyp), not the real feature. Fed synthetic
/// <see cref="PlayerChoice"/> data by ControlPanelWindow rather than a real
/// room roster.
/// </summary>
public partial class AddSlotPickerPrototypeViewModel : ObservableObject
{
    private readonly List<SelectableSlotRow> _allRows;

    /// <summary>The subset of <see cref="_allRows"/> matching <see cref="SearchText"/> right now - what the checkbox list actually shows.</summary>
    public ObservableCollection<SelectableSlotRow> FilteredRows { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyNow))]
    [NotifyPropertyChangedFor(nameof(ApplyButtonText))]
    private int _selectedCount;

    /// <summary>Mirrors SlotPicker.vue's canApply - the Apply button stays disabled until at least one row is checked.</summary>
    public bool CanApplyNow => SelectedCount > 0;

    public string ApplyButtonText => SelectedCount <= 1 ? "Add slot" : $"Add {SelectedCount} slots";

    public bool HasNoResults => FilteredRows.Count == 0;

    public AddSlotPickerPrototypeViewModel(IEnumerable<PlayerChoice> availablePlayers)
    {
        _allRows = availablePlayers.Select(player =>
        {
            var row = new SelectableSlotRow { Player = player };
            row.PropertyChanged += OnRowPropertyChanged;
            return row;
        }).ToList();

        FilteredRows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoResults));
        RefreshFilter();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableSlotRow.IsSelected))
        {
            SelectedCount = _allRows.Count(r => r.IsSelected);
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    /// <summary>Rebuilds <see cref="FilteredRows"/> from <see cref="_allRows"/> by name (matches SlotPicker.vue's "filter by name or game" - there's no "game" here, so both SlotName and DisplayText are matched).</summary>
    private void RefreshFilter()
    {
        var query = SearchText.Trim();
        FilteredRows.Clear();

        foreach (var row in _allRows)
        {
            if (query.Length == 0 ||
                row.Player.SlotName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Player.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRows.Add(row);
            }
        }
    }

    /// <summary>Selects every row currently shown by the filter - 1:1 SlotPicker.vue's selectVisible.</summary>
    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var row in FilteredRows)
        {
            row.IsSelected = true;
        }
    }

    /// <summary>Deselects every row currently shown by the filter - 1:1 SlotPicker.vue's deselectVisible.</summary>
    [RelayCommand]
    private void DeselectVisible()
    {
        foreach (var row in FilteredRows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>
    /// Precedence per slot: its own override (if typed) wins, else null -
    /// meaning "fall back to the group's own shared password", exactly like
    /// today's per-slot override field already works. No batch-wide
    /// password here - the group's shared password was already entered
    /// when its leader first connected, before any slot picking happens.
    /// </summary>
    public IReadOnlyList<StagedSlot> BuildResult() =>
        _allRows
            .Where(r => r.IsSelected)
            .Select(r => new StagedSlot
            {
                SlotName = r.Player.SlotName,
                DisplayText = r.Player.DisplayText,
                Password = !string.IsNullOrWhiteSpace(r.OverridePassword)
                    ? r.OverridePassword!.Trim()
                    : null
            })
            .ToList();
}
