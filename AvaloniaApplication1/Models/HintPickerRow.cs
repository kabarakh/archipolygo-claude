namespace Archipolygo.Models;

/// <summary>
/// A row in the "Hint..." picker. In Location mode <see cref="Id"/> is the real location id; in Item mode
/// it stays 0, since item hints are sent by name.
/// </summary>
public sealed record HintPickerRow
{
    public long Id { get; init; }
    public required string Name { get; init; }
}
