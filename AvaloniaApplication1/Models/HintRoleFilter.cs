namespace Archipolygo.Models;

/// <summary>Filters a tab's hints by the slot's role: finder (location owner), receiver, or either.</summary>
public enum HintRoleFilter
{
    All,

    /// <summary>This slot owns the location, i.e. has to find and send the item.</summary>
    IFind,

    /// <summary>This slot receives the item once found.</summary>
    IReceive,
}
