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
    /// Shallow copy - lets an editor (see <see cref="ViewModels.SettingsViewModel"/>)
    /// change only the fields it owns and carry every other one through
    /// untouched, instead of having to remember to copy each new field by hand.
    /// </summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
