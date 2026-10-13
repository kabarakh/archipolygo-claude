namespace Archipolygo.Models;

/// <summary>Filters a tab's event log by <see cref="EventEntry.Type"/>.</summary>
public enum EventCategoryFilter
{
    All,
    Hints,
    Items,
    Chat,

    /// <summary>Offered only where an "I'm the admin" server exists.</summary>
    Admin
}
