using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>
/// Which filter bars are expanded. One shared instance, so collapsing filters applies to every tab and
/// detached window instead of flipping back on tab switch. Expanded by default for discoverability.
/// </summary>
public partial class FilterBarLayoutState : ObservableObject
{
    [ObservableProperty]
    private bool _eventsFiltersExpanded = true;

    /// <summary>Hints/Items panel filters, including slot filter and search.</summary>
    [ObservableProperty]
    private bool _rightPanelFiltersExpanded = true;

    [ObservableProperty]
    private bool _dashboardEventsFiltersExpanded = true;

    [ObservableProperty]
    private bool _dashboardHintsFiltersExpanded = true;
}
