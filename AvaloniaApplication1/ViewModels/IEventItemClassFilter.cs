namespace Archipolygo.ViewModels;

/// <summary>
/// Item-class checkboxes of an Events list; lets tabs and the Dashboard share one
/// <see cref="Views.ItemClassFilterButton"/>, since compiled bindings need a single <c>x:DataType</c>.
/// </summary>
public interface IEventItemClassFilter
{
    bool ShowProgressionItemEvents { get; set; }
    bool ShowUsefulItemEvents { get; set; }
    bool ShowFillerItemEvents { get; set; }
    bool ShowTrapItemEvents { get; set; }

    /// <summary>"Classes" while every class is shown, "Classes 3/4" otherwise.</summary>
    string EventItemClassFilterButtonText { get; }

    bool IsEventItemClassFilterActive { get; }
}

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
