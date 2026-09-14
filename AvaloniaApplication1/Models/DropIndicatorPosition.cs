namespace Archipolygo.Models;

/// <summary>
/// Where, relative to a <c>GroupViewModel</c>-templated tab header or
/// Dashboard Overview row, a manual tab-reorder drag (Feature-Plaene/Tab-Reihenfolge.md)
/// is currently hovering - drives a thin insertion-line indicator at that
/// element's own leading/trailing edge, rather than highlighting the whole
/// element. <see cref="None"/> means this element isn't the current drop
/// target at all.
/// </summary>
public enum DropIndicatorPosition
{
    None,
    Before,
    After
}
