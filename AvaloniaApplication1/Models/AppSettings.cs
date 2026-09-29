using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Archipolygo.Models;

/// <summary>
/// Global, application-wide settings (not tied to a single profile).
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Default value for <see cref="ServerConnectionGroup.AutoConnect"/> when creating a new server.
    /// </summary>
    public bool DefaultAutoConnect { get; set; }

    /// <summary>
    /// Maximum number of <see cref="EventEntry"/> items kept per tab; oldest entries are
    /// dropped once the limit is exceeded.
    /// </summary>
    public int EventHistoryLimit { get; set; } = 2000;

    /// <summary>
    /// "System" (follow the OS theme), "Light", or "Dark" - see
    /// <see cref="Services.ThemeService"/>. Plain string rather than an enum
    /// so <c>System.Text.Json</c> doesn't need a registered
    /// <c>JsonStringEnumConverter</c> (which <see cref="Services.PersistenceService"/>
    /// doesn't otherwise set up). Missing from an older <c>settings.json</c>
    /// simply deserializes to this default - no migration code needed, same
    /// as every other additive field here.
    /// </summary>
    public string ThemePreference { get; set; } = "System";

    /// <summary>
    /// "Compact" or "Normal" - see <see cref="Services.DensityService"/>.
    /// Compact by default, including for an older <c>settings.json</c> that
    /// predates this field (Kompakteres-Layout.md, decision 1:
    /// the denser layout was the whole point of adding this, so existing
    /// installs get it too rather than having to opt in). Plain string for
    /// the same reason as <see cref="ThemePreference"/>.
    /// </summary>
    public string UiDensity { get; set; } = Services.DensityService.Compact;

    // Which filter bars are expanded - see ViewModels.FilterBarLayoutState
    // (Kompakteres-Layout.md, Teil D). Expanded by default,
    // including for an older settings.json without these fields.
    public bool EventsFiltersExpanded { get; set; } = true;
    public bool RightPanelFiltersExpanded { get; set; } = true;
    public bool DashboardEventsFiltersExpanded { get; set; } = true;
    public bool DashboardHintsFiltersExpanded { get; set; } = true;

    /// <summary>
    /// "Off", "Count", or "UntilFocus" - see <see cref="Models.AttentionBlinkMode"/>
    /// and <see cref="BlinkMode"/>. Plain string for the same reason as
    /// <see cref="ThemePreference"/>; an unrecognized value reads as the
    /// default rather than failing.
    /// </summary>
    public string AttentionBlinkMode { get; set; } = nameof(Models.AttentionBlinkMode.Count);

    /// <summary>How many times a Windows taskbar button blinks per attention request in "Count" mode (macOS has no equivalent - it bounces once).</summary>
    public int AttentionBlinkCount { get; set; } = 4;

    /// <summary>
    /// Keyed by <see cref="AttentionCategory"/> name. Read through
    /// <see cref="GetAttentionCategorySetting"/>, never directly: a key
    /// missing here (an older <c>settings.json</c>, or a category added in a
    /// later version) must fall back to that category's own default, not
    /// silently read as "off".
    /// </summary>
    public Dictionary<string, AttentionCategorySetting> AttentionCategories { get; set; } = AttentionCategorySetting.CreateDefaults();

    /// <summary>Prefix window titles with the unread attention count, e.g. "(3) Archipolygo" - see <see cref="ViewModels.MainWindowViewModel.MainWindowTitle"/>.</summary>
    public bool ShowUnreadInTitle { get; set; } = true;

    /// <summary>Show the same count as a native badge - Windows taskbar-button overlay, macOS Dock badge (see <see cref="Services.IUnreadBadgeService"/>).</summary>
    public bool ShowUnreadBadge { get; set; } = true;

    [JsonIgnore]
    public Models.AttentionBlinkMode BlinkMode =>
        Enum.TryParse<Models.AttentionBlinkMode>(AttentionBlinkMode, ignoreCase: true, out var mode) ? mode : Models.AttentionBlinkMode.Count;

    public AttentionCategorySetting GetAttentionCategorySetting(AttentionCategory category) =>
        AttentionCategories.TryGetValue(category.ToString(), out var setting) && setting is not null
            ? setting
            : AttentionCategorySetting.DefaultFor(category);

    /// <summary>
    /// Shallow copy - lets an editor (see <see cref="ViewModels.SettingsViewModel"/>)
    /// change only the fields it owns and carry every other one through
    /// untouched, instead of having to remember to copy each new field by hand.
    /// Reference-typed fields are the exception: <see cref="AttentionCategories"/>
    /// gets its own copy, so editing the clone can't leak into the original
    /// before the dialog is actually saved.
    /// </summary>
    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.AttentionCategories = AttentionCategories.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
        return clone;
    }
}
