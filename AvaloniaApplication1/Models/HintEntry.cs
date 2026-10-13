using System;
using Archipelago.MultiClient.Net.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>A single hint. Observable rather than immutable because <see cref="Found"/> changes live.</summary>
public partial class HintEntry : ObservableObject
{
    /// <summary>
    /// Synthetic key (receiving player, finding player, item id, location id) - Archipelago hints have no native id.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The configured slot this hint belongs to, for the slot filter on the merged list.</summary>
    public Guid SlotId { get; init; }

    public int ReceivingPlayer { get; init; }

    public int FindingPlayer { get; init; }

    public string ReceivingPlayerName { get; init; } = string.Empty;

    public string FindingPlayerName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string LocationName { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;

    public bool IsNewSinceLastSession { get; init; }

    public ItemFlags ItemFlags { get; init; }

    public EventTextSegmentKind ItemKind { get; init; }

    public EventTextSegmentKind ReceivingPlayerKind { get; init; }

    public EventTextSegmentKind FindingPlayerKind { get; init; }

    [ObservableProperty]
    private bool _found;
}
