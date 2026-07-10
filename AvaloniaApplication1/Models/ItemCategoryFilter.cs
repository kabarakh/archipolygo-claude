namespace Archipolygo.Models;

/// <summary>
/// Filter applied to the received-items list based on the item's category
/// (derived from its Archipelago item flags).
/// </summary>
public enum ItemCategoryFilter
{
    /// <summary>Show all items regardless of category.</summary>
    All,

    /// <summary>Show only progression / advancement items.</summary>
    Progress,

    /// <summary>Show only useful (never-exclude) items.</summary>
    Useful,

    /// <summary>Show only filler / normal items.</summary>
    Normal,

    /// <summary>Show only trap items.</summary>
    Trap,
}
