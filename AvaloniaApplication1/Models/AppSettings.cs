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
}
