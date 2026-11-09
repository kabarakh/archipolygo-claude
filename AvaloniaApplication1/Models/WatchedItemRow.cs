using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>One progression item the watched slot received, as shown in the strip above the event log.</summary>
public sealed partial class WatchedItemRow : ObservableObject
{
    public required string ItemName { get; init; }

    public required string SenderName { get; init; }

    public required string LocationName { get; init; }

    /// <summary>The item-link group the item was sent to, when the watched slot got it as one of its members.</summary>
    public string? ItemLinkGroupName { get; init; }

    public string? ViaText => ItemLinkGroupName is null ? null : $"via {ItemLinkGroupName}";

    public bool HasViaText => ItemLinkGroupName is not null;

    public string LocationText => $"({LocationName})";

    /// <summary>Until the group is next seen; see <see cref="Services.IAttentionTracker.NoteNewWatchedItems"/>.</summary>
    [ObservableProperty]
    private bool _isNew;
}
