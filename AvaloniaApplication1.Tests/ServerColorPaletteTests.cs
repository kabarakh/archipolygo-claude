using Archipolygo.Models;

namespace AvaloniaApplication1.Tests;

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
        // 9 of 10 colors taken once each - the 10th is the only unused one.
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
        // The least-used color wins even though it isn't the lowest index among those assigned.
        var alreadyAssigned = new[] { ServerColorPalette.Colors[0], ServerColorPalette.Colors[0], ServerColorPalette.Colors[1] };

        var assigned = ServerColorPalette.AssignColor(alreadyAssigned);

        Assert.Equal(ServerColorPalette.Colors[2], assigned);
    }
}
