using System;
using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// Persisted sync state per profile: used to detect, after a new login,
/// which items/hints have arrived since the last logout
/// (see Phase 2/4 in the implementation plan).
/// </summary>
public class ProfileSyncState
{
    public Guid ProfileId { get; set; }

    /// <summary>
    /// Highest item index that has already been shown to the user.
    /// </summary>
    public int LastSeenItemIndex { get; set; }

    /// <summary>
    /// Synthetic keys (see <see cref="HintEntry.Key"/>) of all hints that have
    /// already been shown to the user. Archipelago hints have no native id,
    /// so a composite key is used instead of a single sequential index.
    /// </summary>
    public HashSet<string> SeenHintIds { get; set; } = new();
}
