namespace Archipolygo.Models;

/// <summary>
/// Which panel is shown in the Dashboard's left column: the per-server
/// Overview list, or the shared, server-spanning Events view (see
/// Feature-Plaene/Archiv/Dashboard-Tab.md's follow-up "geteilte Event-Ansicht").
/// Same role as <see cref="RightPanelView"/> for a tab's own Hints/Items
/// switch - a Button+IsVisible toggle, not a real <c>TabControl</c> (see
/// that plan's "Warum nicht als echtes TabItem" reasoning for why a second
/// real TabControl nested inside the Dashboard would reintroduce the same
/// lazy-ContentTemplate-materialization gotcha).
/// </summary>
public enum DashboardLeftPanel
{
    Overview,
    Events,
}
