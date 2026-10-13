using System.Collections.Generic;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Builds colored <see cref="EventTextSegment"/> runs for log lines. Item colors mirror the official
/// Archipelago client's palette, which players already associate with those flags.
/// </summary>
public static class EventSegmentBuilder
{
    public static IReadOnlyList<EventTextSegment> BuildChatSegments(LogMessage logMessage, IReadOnlySet<int> otherConnectedSlotIds)
    {
        var segments = new List<EventTextSegment>(logMessage.Parts.Length);

        foreach (var part in logMessage.Parts)
        {
            var kind = part switch
            {
                PlayerMessagePart playerPart => ClassifyPlayer(playerPart, otherConnectedSlotIds),
                ItemMessagePart itemPart => ClassifyItemFlags(itemPart.Flags),
                _ => EventTextSegmentKind.PlainText
            };

            segments.Add(new EventTextSegment(part.Text ?? string.Empty, kind));
        }

        return segments;
    }

    public static IReadOnlyList<EventTextSegment> BuildItemReceivedSegments(string itemDisplayName, ItemFlags flags, string locationDisplayName) =>
        new[]
        {
            new EventTextSegment("Received ", EventTextSegmentKind.PlainText),
            new EventTextSegment(itemDisplayName, ClassifyItemFlags(flags)),
            new EventTextSegment($" ({locationDisplayName})", EventTextSegmentKind.PlainText)
        };

    public static IReadOnlyList<EventTextSegment> BuildHintReceivedSegments(
        string itemName, ItemFlags itemFlags,
        string findingPlayerName, EventTextSegmentKind findingPlayerKind,
        string receivingPlayerName, EventTextSegmentKind receivingPlayerKind,
        string locationName) =>
        new[]
        {
            new EventTextSegment("Hint: ", EventTextSegmentKind.PlainText),
            new EventTextSegment(itemName, ClassifyItemFlags(itemFlags)),
            new EventTextSegment(" (", EventTextSegmentKind.PlainText),
            new EventTextSegment(findingPlayerName, findingPlayerKind),
            new EventTextSegment(" -> ", EventTextSegmentKind.PlainText),
            new EventTextSegment(receivingPlayerName, receivingPlayerKind),
            new EventTextSegment($", {locationName})", EventTextSegmentKind.PlainText)
        };

    public static IReadOnlyList<EventTextSegment> BuildConnectedSegments(string slotName) =>
        new[]
        {
            new EventTextSegment("Connected as ", EventTextSegmentKind.PlainText),
            new EventTextSegment(slotName, EventTextSegmentKind.OwnSlotName),
            new EventTextSegment(".", EventTextSegmentKind.PlainText)
        };

    public static IReadOnlyList<EventTextSegment> BuildDisconnectedSegments(string slotName, string reason) =>
        new[]
        {
            new EventTextSegment("Disconnected as ", EventTextSegmentKind.PlainText),
            new EventTextSegment(slotName, EventTextSegmentKind.OwnSlotName),
            new EventTextSegment($" ({reason}).", EventTextSegmentKind.PlainText)
        };

    /// <summary>
    /// <see cref="Archipelago.MultiClient.Net.BounceFeatures.DeathLink.DeathLink.Cause"/> is already full display text but may be null.
    /// </summary>
    public static IReadOnlyList<EventTextSegment> BuildDeathLinkSegments(string sourcePlayerName, string? cause) =>
        new[]
        {
            new EventTextSegment("DeathLink: ", EventTextSegmentKind.PlainText),
            new EventTextSegment(string.IsNullOrEmpty(cause) ? $"{sourcePlayerName} died." : cause, EventTextSegmentKind.DeathLink)
        };

    public static EventTextSegmentKind ClassifyPlayerSlot(int slotId, int ownSlotId, IReadOnlySet<int> otherConnectedSlotIds)
    {
        if (slotId == ownSlotId)
        {
            return EventTextSegmentKind.OwnSlotName;
        }

        return otherConnectedSlotIds.Contains(slotId)
            ? EventTextSegmentKind.ConnectedSlotName
            : EventTextSegmentKind.OtherSlotName;
    }

    private static EventTextSegmentKind ClassifyPlayer(PlayerMessagePart playerPart, IReadOnlySet<int> otherConnectedSlotIds)
    {
        if (playerPart.IsActivePlayer)
        {
            return EventTextSegmentKind.OwnSlotName;
        }

        return otherConnectedSlotIds.Contains(playerPart.SlotId)
            ? EventTextSegmentKind.ConnectedSlotName
            : EventTextSegmentKind.OtherSlotName;
    }

    // Priority order matches Archipelago.MultiClient.Net.Colors.ColorUtils.GetColor(ItemFlags),
    // so a combination of flags resolves to the same color other AP clients would show.
    public static EventTextSegmentKind ClassifyItemFlags(ItemFlags flags)
    {
        if (flags.HasFlag(ItemFlags.Advancement))
        {
            return EventTextSegmentKind.ItemProgression;
        }

        if (flags.HasFlag(ItemFlags.NeverExclude))
        {
            return EventTextSegmentKind.ItemUseful;
        }

        if (flags.HasFlag(ItemFlags.Trap))
        {
            return EventTextSegmentKind.ItemTrap;
        }

        return EventTextSegmentKind.ItemOther;
    }
}
