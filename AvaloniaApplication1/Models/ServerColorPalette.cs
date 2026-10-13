using System.Collections.Generic;
using System.Linq;

namespace Archipolygo.Models;

/// <summary>Fixed palette of named Avalonia brushes used to tell servers apart across the app.</summary>
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
    /// Light-theme counterparts by index: same hue, darkened until WCAG contrast against white is at least 4.5:1
    /// (the <see cref="Colors"/> entries only read well on dark backgrounds).
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
    /// Swaps a palette color for its light variant in the Light theme; a manually picked RGB color has no
    /// known light variant and passes through unchanged.
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

    /// <summary>Least-used palette color, ties broken by palette order - so it wraps around once all are taken.</summary>
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
