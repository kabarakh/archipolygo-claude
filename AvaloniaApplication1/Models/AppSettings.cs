namespace Archipolygo.Models;

/// <summary>
/// Global, application-wide settings (not tied to a single profile).
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Default value for <see cref="ServerProfile.AutoConnect"/> when creating a new profile.
    /// </summary>
    public bool DefaultAutoConnect { get; set; }

    /// <summary>
    /// Maximum number of <see cref="EventEntry"/> items kept per tab; oldest entries are
    /// dropped once the limit is exceeded.
    /// </summary>
    public int EventHistoryLimit { get; set; } = 500;
}
