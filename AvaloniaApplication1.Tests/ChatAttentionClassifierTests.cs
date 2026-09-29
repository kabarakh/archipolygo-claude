using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): which chat lines count as a
/// <see cref="AttentionCategory.ChatMention"/> vs. plain
/// <see cref="AttentionCategory.Chat"/> - see <see cref="ChatAttentionClassifier"/>.
/// </summary>
public class ChatAttentionClassifierTests
{
    private static readonly string?[] OwnNames = { "Alice", null, "Link" };

    [Fact]
    public void OwnSlotAsSender_IsNeverReported()
    {
        Assert.Null(ChatAttentionClassifier.Classify(senderIsOwnSlot: true, "hey Alice", OwnNames));
    }

    [Theory]
    [InlineData("alice can you check the forest?")]
    [InlineData("@Alice")]
    [InlineData("thanks, ALICE!")]
    [InlineData("Link")]
    public void MentionAtWordBoundary_IsChatMention(string message)
    {
        Assert.Equal(AttentionCategory.ChatMention, ChatAttentionClassifier.Classify(false, message, OwnNames));
    }

    [Theory]
    [InlineData("anyone got a DeathLink going?")]
    [InlineData("Alicent is my favorite")]
    [InlineData("gg everyone")]
    public void NoMention_IsPlainChat(string message)
    {
        Assert.Equal(AttentionCategory.Chat, ChatAttentionClassifier.Classify(false, message, OwnNames));
    }

    /// <summary>A first, glued-on occurrence mustn't hide a later genuine one.</summary>
    [Fact]
    public void LaterStandaloneOccurrence_AfterGluedOne_IsFound()
    {
        Assert.True(ChatAttentionClassifier.ContainsAtWordBoundary("DeathLink? Link, you there?", "Link"));
    }
}
