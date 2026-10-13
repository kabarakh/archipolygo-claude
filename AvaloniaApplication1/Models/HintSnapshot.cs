using System;
using Archipelago.MultiClient.Net.Enums;

namespace Archipolygo.Models;

/// <summary>
/// A hint with already-resolved display names, handed from ConnectionManager (which has the session's
/// name lookups) to <see cref="Services.IHintService"/> (which never touches the session).
/// </summary>
public class HintSnapshot
{
    public required string Key { get; init; }

    public Guid SlotId { get; init; }

    public int ReceivingPlayer { get; init; }

    public int FindingPlayer { get; init; }

    public string ReceivingPlayerName { get; init; } = string.Empty;

    public string FindingPlayerName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string LocationName { get; init; } = string.Empty;

    public bool Found { get; init; }

    public ItemFlags ItemFlags { get; init; }

    public EventTextSegmentKind ReceivingPlayerKind { get; init; }

    public EventTextSegmentKind FindingPlayerKind { get; init; }
}
