namespace Archipolygo.Models;

/// <summary>
/// One of a slot's own not-yet-found, not-yet-hinted locations, as offered
/// by the "Hint..." picker's Location mode (see
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md). Sourced from
/// <c>IConnectionManager.GetHintableLocationsAsync</c>, which already
/// excludes checked locations via <c>ILocationCheckHelper.AllMissingLocations</c>
/// - the picker itself additionally excludes already-hinted ones by
/// cross-referencing <c>GroupViewModel.Hints</c>.
/// </summary>
public sealed record HintableLocation
{
    public required long LocationId { get; init; }
    public required string Name { get; init; }
}
