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
    /// Same order/index as <see cref="Colors"/> - Light-theme counterparts,
    /// same hue but hand-derived (lightness lowered until WCAG contrast
    /// against white reaches at least 4.5:1, computed rather than guessed)
    /// since every <see cref="Colors"/> entry already tests well against a
    /// dark background but poorly against a light one - see
    /// Feature-Plaene/Theme-Umschalter-und-Server-Farbwaehler.md's "Konkrete
    /// Farbwerte" table. Only used via <see cref="ResolveDisplayColor"/>.
    /// </summary>
    private static readonly IReadOnlyList<string> LightColors = new[]
    {
        "#0074E6",
        "#2D8654",
        "#E72300",
        "#8B7500",
        "#C633C1",
        "#168579",
        "#DB3B00",
        "#3071E7",
        "#61811F",
        "#E70074"
    };

    /// <summary>
    /// What a group's stored <see cref="ServerConnectionGroup.Color"/> should
    /// actually be rendered as right now - unchanged for Dark (that's what
    /// <see cref="Colors"/> already contains), swapped for its
    /// <see cref="LightColors"/> counterpart for Light *only* when
    /// <paramref name="storedColor"/> is still exactly one of the automatic
    /// <see cref="Colors"/> values. A manually picked color (see the
    /// Connection Editor's color picker) is a deliberate, free RGB choice
    /// with no known "light variant" to derive - passed through unchanged
    /// either way, since there's nothing meaningful to substitute it with.
    /// </summary>
    public static string ResolveDisplayColor(string storedColor, bool isLightTheme)
    {
        if (!isLightTheme)
        {
            return storedColor;
        }

        for (var i = 0; i < Colors.Count; i++)
        {
            if (Colors[i] == storedColor)
            {
                return LightColors[i];
            }
        }

        return storedColor;
    }

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
