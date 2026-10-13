using Archipolygo.Models;

namespace AvaloniaApplication1.Tests;

public class SlotProfileDisplayNameTests
{
    [Fact]
    public void NoAliasKnownYet_ShowsJustTheSlotName()
    {
        var slot = new SlotProfile { SlotName = "KabaDone" };

        Assert.Equal("KabaDone", slot.DisplayName);
    }

    [Fact]
    public void AliasIdenticalToSlotName_ShowsJustTheSlotName()
    {
        // PlayerInfo.Alias defaults to Name when nobody ran "!alias" - no point showing "X (X)".
        var slot = new SlotProfile { SlotName = "KabaDone", Alias = "KabaDone" };

        Assert.Equal("KabaDone", slot.DisplayName);
    }

    [Fact]
    public void AliasDiffersFromSlotName_ShowsBothInArchipelagosOwnFormat()
    {
        var slot = new SlotProfile { SlotName = "KabaDone", Alias = "KabaHarkinian" };

        Assert.Equal("KabaDone (KabaHarkinian)", slot.DisplayName);
    }

    [Fact]
    public void AliasComparisonIsCaseSensitive_MatchingArchipelagosOwnPlayerInfoContract()
    {
        // Alias defaults to Name verbatim; case-insensitive compare could mask a genuine alias differing only in case.
        var slot = new SlotProfile { SlotName = "KabaDone", Alias = "kabadone" };

        Assert.Equal("KabaDone (kabadone)", slot.DisplayName);
    }

    [Theory]
    [InlineData("KabaHK", "KabaDone (KabaHK)", "KabaHK (KabaDone)")]
    [InlineData("KabaSmashWoL", "KabaDone? (KabaSmashWoL)", "KabaSmashWoL (KabaDone?)")]
    public void AliasAlreadyEmbedsTrailingSlotNameParens_StrippedInsteadOfDoubled(string slotName, string alias, string expected)
    {
        // Observed for linked slots: Alias already comes back as "KabaDone (KabaHK)"; without stripping it doubles up.
        var slot = new SlotProfile { SlotName = slotName, Alias = alias };

        Assert.Equal(expected, slot.DisplayName);
    }

    [Fact]
    public void AliasSuffixStrip_IsCaseInsensitive_ButFinalEqualityCheckStaysCaseSensitive()
    {
        // Stripping is lenient; the remainder is then compared case-sensitively as usual.
        var slot = new SlotProfile { SlotName = "KabaHK", Alias = "kabahk (KABAHK)" };

        Assert.Equal("KabaHK (kabahk)", slot.DisplayName);
    }

    [Fact]
    public void ChangingAlias_RaisesPropertyChangedForDisplayName()
    {
        var slot = new SlotProfile { SlotName = "KabaDone" };
        var raised = false;
        slot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SlotProfile.DisplayName))
            {
                raised = true;
            }
        };

        slot.Alias = "KabaHarkinian";

        Assert.True(raised);
        Assert.Equal("KabaDone (KabaHarkinian)", slot.DisplayName);
    }
}
