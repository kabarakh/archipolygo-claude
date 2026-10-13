using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One of the user's slots on a <see cref="ServerConnectionGroup"/>; Host/Port/shared Password live on the group.
/// </summary>
public partial class SlotProfile : ObservableObject
{
    /// <summary>Preserved from the legacy flat profile's Id on migration, so existing per-slot sync state still applies.</summary>
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private Guid _groupId;

    [ObservableProperty]
    private string _slotName = string.Empty;

    /// <summary>
    /// Optional per-slot override of the group password (some custom-hosted servers need it); null uses the group's.
    /// In-memory only, never persisted - <see cref="RequiresPassword"/> is the persisted stand-in.
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string? _password;

    /// <summary>
    /// Learned from real login attempts, never asked up front: false means "not known to need one", not "doesn't".
    /// Self-healing - flips with each login that succeeds with/without a password or fails for a missing/wrong one.
    /// </summary>
    [ObservableProperty]
    private bool _requiresPassword;

    /// <summary>Learned from the room roster while a leader is connected; null until then. Display only.</summary>
    [ObservableProperty]
    private string? _alias;

    /// <summary>"SlotName (Alias)", Archipelago's own chat convention; just the slot name if no differing alias is known.</summary>
    public string DisplayName => FormatDisplayName(SlotName, Alias);

    public static string FormatDisplayName(string slotName, string? alias)
    {
        var effectiveAlias = StripRedundantSlotName(slotName, alias);
        return string.IsNullOrEmpty(effectiveAlias) || string.Equals(effectiveAlias, slotName, StringComparison.Ordinal)
            ? slotName
            : $"{slotName} ({effectiveAlias})";
    }

    /// <summary>
    /// Strips a trailing " (SlotName)" that grouped/linked slots' PlayerInfo.Alias already carries in practice,
    /// which would otherwise display as "KabaHK (KabaDone (KabaHK))".
    /// </summary>
    public static string? StripRedundantSlotName(string slotName, string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return alias;
        }

        var redundantSuffix = $" ({slotName})";
        return alias.EndsWith(redundantSuffix, StringComparison.OrdinalIgnoreCase)
            ? alias[..^redundantSuffix.Length]
            : alias;
    }

    partial void OnAliasChanged(string? value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnSlotNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    /// <summary>
    /// Set after every successful login for this slot (leader or catch-up), kept live only for the leader.
    /// Persisted; null means never synced, so the UI shows no progress bar rather than 0/0.
    /// </summary>
    [ObservableProperty]
    private int? _locationsChecked;

    [ObservableProperty]
    private int? _locationsTotal;

    public bool HasLocationProgress => LocationsTotal is not null;

    public double LocationsCheckedValue => LocationsChecked ?? 0;

    public double LocationsTotalValue => LocationsTotal ?? 0;

    partial void OnLocationsCheckedChanged(int? value) => OnPropertyChanged(nameof(LocationsCheckedValue));

    partial void OnLocationsTotalChanged(int? value)
    {
        OnPropertyChanged(nameof(LocationsTotalValue));
        OnPropertyChanged(nameof(HasLocationProgress));
    }
}
