using Archipolygo.Models;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): reine Logiktests, kein Avalonia
/// nötig. Siehe Feature-Plaene/Server-Farben.md's "Zuweisungsalgorithmus".
/// </summary>
public sealed class ServerColorPaletteTests
{
    [Fact]
    public void AssignColor_NoExistingColors_ReturnsFirstPaletteColor()
    {
        var assigned = ServerColorPalette.AssignColor(System.Array.Empty<string>());

        Assert.Equal(ServerColorPalette.Colors[0], assigned);
    }

    [Fact]
    public void AssignColor_SomeButNotAllPaletteColorsUsed_ReturnsFirstUnusedOne()
    {
        // 9 of the 10 palette colors already taken, each exactly once - the
        // 10th (last) is the only one still unused.
        var alreadyAssigned = ServerColorPalette.Colors.Take(9);

        var assigned = ServerColorPalette.AssignColor(alreadyAssigned);

        Assert.Equal(ServerColorPalette.Colors[9], assigned);
    }

    [Fact]
    public void AssignColor_EveryPaletteColorUsedExactlyOnce_WrapsAroundToFirstColor()
    {
        var alreadyAssigned = ServerColorPalette.Colors;

        var assigned = ServerColorPalette.AssignColor(alreadyAssigned);

        Assert.Equal(ServerColorPalette.Colors[0], assigned);
    }

    [Fact]
    public void AssignColor_UnevenUsage_ReturnsTheStillUnusedColor_NotJustTheLowestIndex()
    {
        // Colors[0] used twice, Colors[1] used once, Colors[2] never used -
        // the least-used (Colors[2]) must win even though it's not the
        // overall lowest-index color among the ones already assigned.
        var alreadyAssigned = new[] { ServerColorPalette.Colors[0], ServerColorPalette.Colors[0], ServerColorPalette.Colors[1] };

        var assigned = ServerColorPalette.AssignColor(alreadyAssigned);

        Assert.Equal(ServerColorPalette.Colors[2], assigned);
    }
}
