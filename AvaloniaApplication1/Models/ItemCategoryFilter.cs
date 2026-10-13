namespace Archipolygo.Models;

/// <summary>Filters the received-items list by the item's Archipelago flags.</summary>
public enum ItemCategoryFilter
{
    All,

    /// <summary>Progression / advancement items.</summary>
    Progress,

    /// <summary>Useful (never-exclude) items.</summary>
    Useful,

    /// <summary>Filler items.</summary>
    Normal,

    Trap,
}
