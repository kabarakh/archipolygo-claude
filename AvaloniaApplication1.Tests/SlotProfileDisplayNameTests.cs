using Archipolygo.Models;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="SlotProfile.DisplayName"/> -
/// "SlotName (Alias)", the same convention Archipelago's own chat log already
/// uses for a player mention (e.g. "KabaDone (KabaHarkinian) has stopped
/// tracking the game.") - applied everywhere this app shows a configured slot
/// on its own (dropdowns, the slot-management list, ...). Pure property logic,
/// no session/Avalonia involved.
/// </summary>
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
        // The real, common case: PlayerInfo.Alias defaults to the player's
        // Name when nobody ran "!alias" - no point showing "X (X)".
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
        // PlayerInfo.Alias is set verbatim to Name (same case) when nobody
        // set a custom one - a case-insensitive compare here risks masking a
        // genuine custom alias that just happens to differ only in casing.
        var slot = new SlotProfile { SlotName = "KabaDone", Alias = "kabadone" };

        Assert.Equal("KabaDone (kabadone)", slot.DisplayName);
    }

    [Theory]
    [InlineData("KabaHK", "KabaDone (KabaHK)", "KabaHK (KabaDone)")]
    [InlineData("KabaSmashWoL", "KabaDone? (KabaSmashWoL)", "KabaSmashWoL (KabaDone?)")]
    public void AliasAlreadyEmbedsTrailingSlotNameParens_StrippedInsteadOfDoubled(string slotName, string alias, string expected)
    {
        // Observed in practice for grouped/linked slots: PlayerInfo.Alias
        // itself already comes back as "KabaDone (KabaHK)" for a slot named
        // "KabaHK", not just "KabaDone" - without stripping this, DisplayName
        // would double the slot name up as "KabaHK (KabaDone (KabaHK))".
        var slot = new SlotProfile { SlotName = slotName, Alias = alias };

        Assert.Equal(expected, slot.DisplayName);
    }

    [Fact]
    public void AliasSuffixStrip_IsCaseInsensitive_ButFinalEqualityCheckStaysCaseSensitive()
    {
        // The trailing "(SlotName)" being stripped is a cleanup heuristic,
        // not an identity judgement - lenient on purpose. Once stripped
        // though, whether what's left still counts as "no real alias" goes
        // back to the ordinary case-sensitive Alias-vs-SlotName comparison
        // (see AliasComparisonIsCaseSensitive_MatchingArchipelagosOwnPlayerInfoContract).
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
