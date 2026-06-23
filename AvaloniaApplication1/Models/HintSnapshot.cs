using Archipelago.MultiClient.Net.Enums;

namespace Archipolygo.Models;

/// <summary>
/// Plain data carrier for a single hint with already-resolved display names,
/// passed from <see cref="Services.ConnectionManager"/> (which has access to
/// the session's name-lookup helpers) to <see cref="Services.IHintService"/>
/// (which only deals with persistence/diffing, not the Archipelago session).
/// </summary>
public class HintSnapshot
{
    public required string Key { get; init; }

    public int ReceivingPlayer { get; init; }

    public int FindingPlayer { get; init; }

    public string ReceivingPlayerName { get; init; } = string.Empty;

    public string FindingPlayerName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string LocationName { get; init; } = string.Empty;

    public bool Found { get; init; }

    public ItemFlags ItemFlags { get; init; }

    /// <summary>Own/connected/other classification of <see cref="ReceivingPlayer"/>, for log/hint-list coloring.</summary>
    public EventTextSegmentKind ReceivingPlayerKind { get; init; }

    /// <summary>Own/connected/other classification of <see cref="FindingPlayer"/>, for log/hint-list coloring.</summary>
    public EventTextSegmentKind FindingPlayerKind { get; init; }
}
