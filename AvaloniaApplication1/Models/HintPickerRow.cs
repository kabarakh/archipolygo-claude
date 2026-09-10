namespace Archipolygo.Models;

/// <summary>
/// A single row in the "Hint..." picker's list, whichever mode is active
/// (see <see cref="HintTargetMode"/>). For Location mode, <see cref="Id"/>
/// is the real Archipelago location id (usable directly with
/// <c>IConnectionManager.SendHintAsync</c>); Item mode has no equivalent
/// numeric id to send (hinting by item name goes through
/// <c>IConnectionManager.SendItemHintAsync</c> instead, by name), so
/// <see cref="Id"/> is left at 0 there and only <see cref="Name"/> matters.
/// </summary>
public sealed record HintPickerRow
{
    public long Id { get; init; }
    public required string Name { get; init; }
}
