using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

public interface IPersistenceService
{
    /// <summary>Migrates a legacy flat <c>profiles.json</c> once if no groups were ever saved.</summary>
    List<ServerConnectionGroup> LoadGroups();

    void SaveGroups(IEnumerable<ServerConnectionGroup> groups);

    ProfileSyncState LoadSyncState(Guid slotId);

    void SaveSyncState(ProfileSyncState state);

    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);

    /// <summary>Null if nothing is cached yet or the cache file is missing/corrupted.</summary>
    DataPackageCacheEntry? LoadDataPackageCache(Guid groupId, string game);

    void SaveDataPackageCache(Guid groupId, string game, DataPackageCacheEntry entry);

    /// <summary>Called when a server is removed so its cache files aren't orphaned. No-op if nothing was cached.</summary>
    void DeleteDataPackageCacheForGroup(Guid groupId);
}
