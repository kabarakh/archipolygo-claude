using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

public class ProfileSyncStateStore : IProfileSyncStateStore
{
    private readonly IPersistenceService _persistenceService;
    private readonly Dictionary<Guid, ProfileSyncState> _cache = new();

    // Item callbacks can arrive on the socket thread while hint callbacks are
    // dispatched to the UI thread, so Get/Save can legitimately be called
    // concurrently for the same profile - guard the shared cache dictionary.
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
            // Keep the cache pointed at whatever was just persisted (normally
            // the same instance returned by Get, but cheap to be defensive).
            _cache[state.ProfileId] = state;
            _persistenceService.SaveSyncState(state);
        }
    }
}
