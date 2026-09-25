namespace Archipolygo.ViewModels;

/// <summary>
/// The four independent item-class checkboxes narrowing an Events list
/// (<see cref="GroupViewModel"/>'s per-tab list and
/// <see cref="DashboardViewModel"/>'s server-spanning one each keep their own
/// independent set). Exists so both can share one
/// <see cref="Views.ItemClassFilterButton"/> (compiled bindings need a single
/// <c>x:DataType</c>) instead of two hand-copied flyouts - see
/// Kompakteres-Layout.md, Teil C.
/// </summary>
public interface IEventItemClassFilter
{
    bool ShowProgressionItemEvents { get; set; }
    bool ShowUsefulItemEvents { get; set; }
    bool ShowFillerItemEvents { get; set; }
    bool ShowTrapItemEvents { get; set; }

    /// <summary>"Classes" while every class is shown, "Classes 3/4" otherwise - see <see cref="EventItemClassFilterText"/>.</summary>
    string EventItemClassFilterButtonText { get; }

    /// <summary>True while at least one class is hidden - highlights the button like any other active filter.</summary>
    bool IsEventItemClassFilterActive { get; }
}

/// <summary>The one shared implementation of <see cref="IEventItemClassFilter"/>'s derived properties.</summary>
public static class EventItemClassFilterText
{
    public static int ShownCount(IEventItemClassFilter filter) =>
        (filter.ShowProgressionItemEvents ? 1 : 0) + (filter.ShowUsefulItemEvents ? 1 : 0)
        + (filter.ShowFillerItemEvents ? 1 : 0) + (filter.ShowTrapItemEvents ? 1 : 0);

    public static string ButtonText(IEventItemClassFilter filter)
    {
        var shown = ShownCount(filter);
        return shown == 4 ? "Classes" : $"Classes {shown}/4";
    }

    public static bool IsActive(IEventItemClassFilter filter) => ShownCount(filter) < 4;
}
