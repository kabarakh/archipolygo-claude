namespace Archipolygo.Models;

/// <summary>
/// Filter applied to a tab's hint list based on which role this slot plays
/// in the hint: whether it is the one that has to find the item (the location
/// owner) or the one that will receive the item, or both (when finder and
/// receiver are the same slot).
/// </summary>
public enum HintRoleFilter
{
    /// <summary>Show hints regardless of which slot finds or receives.</summary>
    All,

    /// <summary>
    /// Show only hints where this slot owns the location - i.e. it is the one
    /// that has to actually find and send the item.
    /// </summary>
    IFind,

    /// <summary>
    /// Show only hints where this slot is the recipient - i.e. the item will
    /// end up in its inventory once found.
    /// </summary>
    IReceive,
}
