using System;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Single shared in-memory cache + persistence point for each profile's
/// <see cref="ProfileSyncState"/> (last-seen item index and seen-hint ids).
///
/// Both <see cref="IHintService"/> and <see cref="IMessageHistoryService"/>
/// read and write fields on the same <see cref="ProfileSyncState"/> record
/// per profile (one persisted as a single JSON file - see
/// <see cref="PersistenceService.SaveSyncState"/>, which writes the whole
/// object). If each service kept its own independently-loaded copy and
/// called <see cref="IPersistenceService.SaveSyncState"/> on it directly (as
/// used to be the case), whichever one saved last would overwrite the
/// other's already-persisted field with its own stale snapshot - e.g. a hint
/// marked seen by HintService could get silently erased again the next time
/// MessageHistoryService persisted an item-index update, making that hint
/// look "new" again on the next reconnect even though it was received while
/// connected. Routing every read/write through one shared instance per
/// profile avoids that lost-update race.
/// </summary>
public interface IProfileSyncStateStore
{
    ProfileSyncState Get(Guid profileId);

    void Save(ProfileSyncState state);
}
