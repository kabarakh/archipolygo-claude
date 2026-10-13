using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Syncs a group's merged hints with the room-wide snapshot and tracks per-slot seen hint ids
/// so new ones can be flagged after a reconnect/catch-up.
/// </summary>
public interface IHintService
{
    /// <summary>
    /// <paramref name="hints"/> covers the whole room. <paramref name="isLive"/> false (catch-up) still
    /// adds and flags new hints but never reports them to <see cref="IAttentionTracker"/>.
    /// </summary>
    void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints, bool isLive);

    /// <summary>Skips hints already present (same key, or same finder + location); adds no event-log entry.</summary>
    void AddHintFromChat(GroupViewModel group, HintSnapshot snapshot);
}
