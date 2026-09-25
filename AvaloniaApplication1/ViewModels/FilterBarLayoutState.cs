using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// Which filter bars are expanded (Kompakteres-Layout.md,
/// Teil D). One instance, created by <see cref="MainWindowViewModel"/> and
/// shared by every <see cref="GroupViewModel"/> and the
/// <see cref="DashboardViewModel"/>, so collapsing a tab's Events filters
/// collapses them in every tab (and in a detached window) at once instead of
/// flipping back on each tab switch. <see cref="MainWindowViewModel"/>
/// persists changes to <see cref="Models.AppSettings"/>. Expanded by default,
/// so the filters are discoverable on first start.
/// </summary>
public partial class FilterBarLayoutState : ObservableObject
{
    /// <summary>A server tab's Events filters.</summary>
    [ObservableProperty]
    private bool _eventsFiltersExpanded = true;

    /// <summary>A server tab's Hints/Items panel filters (including slot filter and search).</summary>
    [ObservableProperty]
    private bool _rightPanelFiltersExpanded = true;

    /// <summary>The Dashboard's Events panel filters.</summary>
    [ObservableProperty]
    private bool _dashboardEventsFiltersExpanded = true;

    /// <summary>The Dashboard's Hints column filters.</summary>
    [ObservableProperty]
    private bool _dashboardHintsFiltersExpanded = true;
}
