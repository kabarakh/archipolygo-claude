using System.Collections.Generic;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Turns Archipelago.MultiClient.Net's structured chat messages (and the
/// manually built item-received lines) into <see cref="EventTextSegment"/>
/// runs so the UI can color player names and item rarities.
///
/// Player classification relies on data the library already resolves per
/// message part: <see cref="PlayerMessagePart.IsActivePlayer"/> tells us
/// whether a name is this tab's own slot, and comparing
/// <see cref="PlayerMessagePart.SlotId"/> against the slot ids of other tabs
/// currently connected to the same Archipelago server instance (same
/// host+port) tells us whether a name belongs to "someone else tracked by
/// this app" vs. "anyone else in the multiworld".
///
/// Item classification mirrors the official Archipelago client's palette
/// (Plum for Advancement/progression, SlateBlue for NeverExclude/useful,
/// Salmon for Trap, Cyan for everything else) for the "trap/progression/
/// useful/other" colors, since players coming from other Archipelago
/// trackers/clients will already associate those colors with those flags.
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

    /// <summary>
    /// Builds the colored "Hint: {item} ({finder} -&gt; {receiver}, {location})"
    /// log line for a newly-seen hint, matching the same item/player coloring
    /// used for received-item and chat log lines.
    /// </summary>
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

    /// <summary>
    /// Builds the colored "Connected as {slot}." log line, so the slot name
    /// uses the same OwnSlotName color as chat/hints.
    /// </summary>
    public static IReadOnlyList<EventTextSegment> BuildConnectedSegments(string slotName) =>
        new[]
        {
            new EventTextSegment("Connected as ", EventTextSegmentKind.PlainText),
            new EventTextSegment(slotName, EventTextSegmentKind.OwnSlotName),
            new EventTextSegment(".", EventTextSegmentKind.PlainText)
        };

    /// <summary>
    /// Builds the colored "Disconnected as {slot} ({reason})." log line, so
    /// the slot name uses the same OwnSlotName color as chat/hints.
    /// </summary>
    public static IReadOnlyList<EventTextSegment> BuildDisconnectedSegments(string slotName, string reason) =>
        new[]
        {
            new EventTextSegment("Disconnected as ", EventTextSegmentKind.PlainText),
            new EventTextSegment(slotName, EventTextSegmentKind.OwnSlotName),
            new EventTextSegment($" ({reason}).", EventTextSegmentKind.PlainText)
        };

    /// <summary>
    /// Classifies a slot id the same way chat player parts are classified:
    /// the local session's own slot, a slot tracked by another tab of this
    /// app on the same server instance, or anyone else.
    /// </summary>
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
