using System.Collections.Generic;
using System.Linq;

namespace Archipolygo.Models;

/// <summary>
/// The fixed set of colors used to tell servers (<see cref="ServerConnectionGroup"/>s)
/// apart at a glance across the app - tab headers, Dashboard Overview, and the
/// consolidated cross-server Events/Hints lists (see Feature-Plaene/Server-Farben.md).
/// Named Avalonia brushes, same convention as
/// <see cref="Converters.EventTextSegmentKindToBrushConverter"/> - no per-theme
/// variants, since neither that converter nor the app's own <c>App.axaml</c>
/// has any theme-aware color resources to draw from.
/// </summary>
public static class ServerColorPalette
{
    public static readonly IReadOnlyList<string> Colors = new[]
    {
        "DodgerBlue",
        "MediumSeaGreen",
        "Tomato",
        "Gold",
        "Orchid",
        "Turquoise",
        "Coral",
        "CornflowerBlue",
        "YellowGreen",
        "HotPink"
    };

    /// <summary>
    /// Picks the next color for a newly created/still-colorless group, given
    /// the colors every other currently-configured group already has (see
    /// Server-Farben.md's "Zuweisungsalgorithmus"). Counts how many groups
    /// currently hold each palette color and returns the least-used one,
    /// ties broken by palette order - which means the first
    /// <see cref="Colors"/>.Count groups each get a distinct, unused color,
    /// and the palette then deterministically wraps back around to
    /// <see cref="Colors"/>[0] from the next group onward, without any
    /// separate "is the palette full" branch.
    /// </summary>
    public static string AssignColor(IEnumerable<string> alreadyAssignedColors)
    {
        var usageCounts = alreadyAssignedColors
            .GroupBy(color => color)
            .ToDictionary(g => g.Key, g => g.Count());

        return Colors
            .OrderBy(color => usageCounts.TryGetValue(color, out var count) ? count : 0)
            .First();
    }
}
