using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Archipolygo.Models;

/// <summary>Global, application-wide settings, persisted to settings.json.</summary>
public class AppSettings
{
    /// <summary>Default for <see cref="ServerConnectionGroup.AutoConnect"/> on a new server.</summary>
    public bool DefaultAutoConnect { get; set; }

    /// <summary>Max <see cref="EventEntry"/> items kept per tab; oldest are dropped first.</summary>
    public int EventHistoryLimit { get; set; } = 2000;

    /// <summary>
    /// "System", "Light" or "Dark". A plain string rather than an enum so System.Text.Json
    /// needs no JsonStringEnumConverter (the same applies to the other string settings here).
    /// </summary>
    public string ThemePreference { get; set; } = "System";

    /// <summary>"Compact" or "Normal"; Compact also for an older settings.json without this field, deliberately.</summary>
    public string UiDensity { get; set; } = Services.DensityService.Compact;

    // Which filter bars are expanded - see ViewModels.FilterBarLayoutState.
    public bool EventsFiltersExpanded { get; set; } = true;
    public bool RightPanelFiltersExpanded { get; set; } = true;
    public bool DashboardEventsFiltersExpanded { get; set; } = true;
    public bool DashboardHintsFiltersExpanded { get; set; } = true;

    /// <summary>"Off", "Count" or "UntilFocus"; read via <see cref="BlinkMode"/>, where an unrecognized value reads as the default.</summary>
    public string AttentionBlinkMode { get; set; } = nameof(Models.AttentionBlinkMode.Count);

    /// <summary>Windows taskbar blinks per attention request in "Count" mode (macOS bounces once).</summary>
    public int AttentionBlinkCount { get; set; } = 4;

    /// <summary>
    /// Keyed by <see cref="AttentionCategory"/> name. Read only through <see cref="GetAttentionCategorySetting"/>:
    /// a missing key (older file, newer category) must fall back to the category's default, not "off".
    /// </summary>
    public Dictionary<string, AttentionCategorySetting> AttentionCategories { get; set; } = AttentionCategorySetting.CreateDefaults();

    /// <summary>Prefix window titles with the unread attention count, e.g. "(3) Archipolygo".</summary>
    public bool ShowUnreadInTitle { get; set; } = true;

    /// <summary>Show the same count as a native badge (Windows taskbar overlay, macOS Dock badge).</summary>
    public bool ShowUnreadBadge { get; set; } = true;

    [JsonIgnore]
    public Models.AttentionBlinkMode BlinkMode =>
        Enum.TryParse<Models.AttentionBlinkMode>(AttentionBlinkMode, ignoreCase: true, out var mode) ? mode : Models.AttentionBlinkMode.Count;

    public AttentionCategorySetting GetAttentionCategorySetting(AttentionCategory category) =>
        AttentionCategories.TryGetValue(category.ToString(), out var setting) && setting is not null
            ? setting
            : AttentionCategorySetting.DefaultFor(category);

    /// <summary>
    /// Shallow copy so an editor carries untouched fields through automatically; <see cref="AttentionCategories"/>
    /// is deep-copied so edits can't leak into the original before the dialog is saved.
    /// </summary>
    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.AttentionCategories = AttentionCategories.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        return clone;
    }
}
