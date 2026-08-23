using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Keeps a group's merged <see cref="GroupViewModel.Hints"/> collection in
/// sync with the full, room-wide hint snapshot delivered by the leader's
/// Archipelago session, and tracks (per slot, via
/// <see cref="ProfileSyncState.SeenHintIds"/>) which hints have already been
/// shown for that particular slot so new ones can be flagged after a
/// reconnect/catch-up.
/// </summary>
public interface IHintService
{
    /// <summary>
    /// Called whenever the leader session's full current hint list is
    /// available (fires on every (re)connect/catch-up and on every later
    /// change, e.g. a hinted location being checked). <paramref name="hints"/>
    /// covers the whole room; each <see cref="HintSnapshot.SlotId"/> says
    /// which of this group's configured slots that particular hint concerns
    /// (see <see cref="ConnectionManager"/>, which resolves that before
    /// calling this). Adds new hints, updates the <see cref="HintEntry.Found"/>
    /// status of existing ones, and persists which hints have been seen, per
    /// slot.
    /// </summary>
    void SyncHints(GroupViewModel group, IReadOnlyList<HintSnapshot> hints);
}
