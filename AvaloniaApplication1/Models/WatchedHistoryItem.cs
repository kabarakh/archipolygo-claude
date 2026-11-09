using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Services;

namespace Archipolygo.Models;

/// <summary>One item of the watched slot's full received history, as read by a catch-up and already named.</summary>
public sealed record WatchedHistoryItem(string ItemName, string LocationName, string SenderName, EventTextSegmentKind SenderKind, ItemFlags Flags)
{
    public bool IsProgression => EventSegmentBuilder.ClassifyItemFlags(Flags) == EventTextSegmentKind.ItemProgression;
}
