using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>One slot of the room roster as read from the session; <paramref name="DisplayName"/> is already "SlotName (Alias)".</summary>
public sealed record RoomPlayerRef(int SlotNumber, string SlotName, string DisplayName);

/// <summary>An item-link group of the room as read from the session (a roster entry with PlayerInfo.IsGroup).</summary>
public sealed record ItemLinkGroupSnapshot(RoomPlayerRef Group, string Game, IReadOnlyList<RoomPlayerRef> Members);

public sealed record ItemLinkMember(string DisplayName, EventTextSegmentKind Kind, RoomSlotStatus Slot);

/// <summary>Display row of the "Item links" panel.</summary>
public sealed record ItemLinkGroup(string DisplayName, string Game, RoomSlotStatus Slot, IReadOnlyList<ItemLinkMember> Members)
{
    public string MemberCountText => Members.Count == 1 ? "1 member" : $"{Members.Count} members";
}
