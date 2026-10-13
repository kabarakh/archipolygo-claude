using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.TestSupport;

/// <summary>In-memory stand-in: starts with zero groups and silently discards every save.</summary>
public sealed class FakePersistenceService : IPersistenceService
{
    public List<ServerConnectionGroup> LoadGroups() => new();

    public void SaveGroups(IEnumerable<ServerConnectionGroup> groups)
    {
    }

    public ProfileSyncState LoadSyncState(Guid slotId) => new() { ProfileId = slotId };

    public void SaveSyncState(ProfileSyncState state)
    {
    }

    public AppSettings LoadSettings() => new();

    public void SaveSettings(AppSettings settings)
    {
    }

    public DataPackageCacheEntry? LoadDataPackageCache(Guid groupId, string game) => null;

    public void SaveDataPackageCache(Guid groupId, string game, DataPackageCacheEntry entry)
    {
    }

    public void DeleteDataPackageCacheForGroup(Guid groupId)
    {
    }
}
