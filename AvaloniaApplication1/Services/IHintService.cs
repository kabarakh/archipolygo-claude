using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Keeps a tab's <see cref="TabViewModel.Hints"/> collection in sync with the
/// full hint snapshot delivered by the Archipelago session, and tracks (per
/// profile, via <see cref="ProfileSyncState.SeenHintIds"/>) which hints have
/// already been shown so new ones can be flagged on reconnect.
/// </summary>
public interface IHintService
{
    /// <summary>
    /// Called whenever the session's full current hint list is available
    /// (fires on connect and on every later change, e.g. a hinted location
    /// being checked). Adds new hints, updates the <see cref="HintEntry.Found"/>
    /// status of existing ones, and persists which hints have been seen.
    /// </summary>
    void SyncHints(TabViewModel tab, ServerProfile profile, IReadOnlyList<HintSnapshot> hints);
}
