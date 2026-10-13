namespace Archipolygo.Models;

/// <summary>
/// Which pool the "Hint..." picker searches, mirroring <c>!hint &lt;item&gt;</c> (Item, the default)
/// vs. <c>!hint_location &lt;location&gt;</c> (Location).
/// </summary>
public enum HintTargetMode
{
    Item,
    Location,
}
