using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Classifies player chat lines. Free of library types because <c>ChatLogMessage</c> has only an internal
/// constructor, which would make the rules untestable.
/// </summary>
public static class ChatAttentionClassifier
{
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

    /// <summary>Word boundaries, so a slot named "Link" doesn't match every "DeathLink".</summary>
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
