using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Decides which <see cref="AttentionCategory"/> (if any) a player chat
/// line falls into - kept free of any <c>Archipelago.MultiClient.Net</c>
/// types (whose <c>ChatLogMessage</c> has only an internal constructor) so
/// the rules are directly unit-testable. See the feature-plan archive's
/// <c>Benachrichtigungen.md</c>, section "Kategorien".
/// </summary>
public static class ChatAttentionClassifier
{
    /// <param name="senderIsOwnSlot">Whether the sender is any of this group's configured slots - your own messages never ask for your attention.</param>
    /// <param name="ownNames">Every configured slot's name and alias - a match anywhere at word boundaries makes it a <see cref="AttentionCategory.ChatMention"/>.</param>
    public static AttentionCategory? Classify(bool senderIsOwnSlot, string message, IEnumerable<string?> ownNames)
    {
        if (senderIsOwnSlot)
        {
            return null;
        }

        foreach (var name in ownNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && ContainsAtWordBoundary(message, name))
            {
                return AttentionCategory.ChatMention;
            }
        }

        return AttentionCategory.Chat;
    }

    /// <summary>
    /// Case-insensitive (same <see cref="StringComparison.OrdinalIgnoreCase"/>
    /// as every other slot name comparison here), and only where the match
    /// isn't glued to further letters/digits - otherwise a slot named "Link"
    /// would count every "DeathLink" as a mention.
    /// </summary>
    internal static bool ContainsAtWordBoundary(string text, string word)
    {
        var start = 0;
        while (start <= text.Length - word.Length)
        {
            var index = text.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var end = index + word.Length;
            var boundaryBefore = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var boundaryAfter = end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (boundaryBefore && boundaryAfter)
            {
                return true;
            }

            start = index + 1;
        }

        return false;
    }
}
