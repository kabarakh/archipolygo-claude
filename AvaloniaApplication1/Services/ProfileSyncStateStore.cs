using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

public class ProfileSyncStateStore : IProfileSyncStateStore
{
    private readonly IPersistenceService _persistenceService;
    private readonly Dictionary<Guid, ProfileSyncState> _cache = new();

    // Item callbacks run on the socket thread, hint callbacks on the UI thread.
    private readonly object _lock = new();

    public ProfileSyncStateStore(IPersistenceService persistenceService)
    {
        _persistenceService = persistenceService;
    }

    public ProfileSyncState Get(Guid profileId)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(profileId, out var state))
            {
                state = _persistenceService.LoadSyncState(profileId);
                _cache[profileId] = state;
            }

            return state;
        }
    }

    public void Save(ProfileSyncState state)
    {
        lock (_lock)
        {
            _cache[state.ProfileId] = state;
            _persistenceService.SaveSyncState(state);
        }
    }
}
