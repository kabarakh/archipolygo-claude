using System;
using Archipelago.MultiClient.Net.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// A single hint for this slot. <see cref="Found"/> can change live (when the
/// hinted location is checked), so this is an <see cref="ObservableObject"/>
/// rather than an immutable record, unlike <see cref="EventEntry"/>.
/// </summary>
public partial class HintEntry : ObservableObject
{
    /// <summary>
    /// Synthetic composite key identifying this hint. The Archipelago
    /// <c>Hint</c> model has no native id, so receiving player, finding
    /// player, item id and location id together are used as a stable key.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    public int ReceivingPlayer { get; init; }

    public int FindingPlayer { get; init; }

    public string ReceivingPlayerName { get; init; } = string.Empty;

    public string FindingPlayerName { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string LocationName { get; init; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;

    public bool IsNewSinceLastSession { get; init; }

    public ItemFlags ItemFlags { get; init; }

    /// <summary>Segment kind for <see cref="ItemName"/>'s foreground (trap/progression/useful/other).</summary>
    public EventTextSegmentKind ItemKind { get; init; }

    /// <summary>Segment kind for <see cref="ReceivingPlayerName"/>'s foreground (own/connected/other slot).</summary>
    public EventTextSegmentKind ReceivingPlayerKind { get; init; }

    /// <summary>Segment kind for <see cref="FindingPlayerName"/>'s foreground (own/connected/other slot).</summary>
    public EventTextSegmentKind FindingPlayerKind { get; init; }

    [ObservableProperty]
    private bool _found;
}
