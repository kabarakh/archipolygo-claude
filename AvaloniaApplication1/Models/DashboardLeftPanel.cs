namespace Archipolygo.Models;

/// <summary>
/// Which panel the Dashboard's left column shows. A Button+IsVisible toggle, deliberately not a nested
/// TabControl, which would reintroduce the lazy ContentTemplate materialization gotcha.
/// </summary>
public enum DashboardLeftPanel
{
    Overview,
    Events,
}
