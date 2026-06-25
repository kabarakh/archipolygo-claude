namespace Archipolygo.Models;

/// <summary>
/// Filter applied to a tab's event log based on <see cref="EventEntry.Type"/>.
/// </summary>
public enum EventCategoryFilter
{
    /// <summary>Show events of every type.</summary>
    All,

    /// <summary>Show only <see cref="EventType.HintReceived"/> entries.</summary>
    Hints,

    /// <summary>Show only <see cref="EventType.ItemReceived"/> entries.</summary>
    Items
}
