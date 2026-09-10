namespace Archipolygo.Models;

/// <summary>
/// Which pool the "Hint..." picker searches - see
/// <see cref="ViewModels.HintPickerViewModel"/>. Mirrors the two existing
/// chat commands it replaces: <c>!hint &lt;itemname&gt;</c> (Item, the
/// default) vs. <c>!hint_location &lt;locationname&gt;</c> (Location).
/// </summary>
public enum HintTargetMode
{
    Item,
    Location,
}
