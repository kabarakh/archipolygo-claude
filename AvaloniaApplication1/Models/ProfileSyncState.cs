using System;
using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>Persisted per-slot record of what was already shown, to detect items/hints that arrived since the last session.</summary>
public class ProfileSyncState
{
    public Guid ProfileId { get; set; }

    public int LastSeenItemIndex { get; set; }

    /// <summary><see cref="HintEntry.Key"/>s already shown - hints have no sequential index to track instead.</summary>
    public HashSet<string> SeenHintIds { get; set; } = new();
}
