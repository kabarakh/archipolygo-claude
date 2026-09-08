using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Minimal in-memory <see cref="IProfileSyncStateStore"/> for tests - unlike
/// the real <see cref="ProfileSyncStateStore"/>, never touches disk. Lets a
/// test seed "already seen" state (see <see cref="Seed"/>) before exercising
/// <see cref="IHintService.SyncHints"/>.
/// </summary>
public sealed class InMemoryProfileSyncStateStore : IProfileSyncStateStore
{
    private readonly Dictionary<Guid, ProfileSyncState> _states = new();

    public void Seed(ProfileSyncState state) => _states[state.ProfileId] = state;

    public ProfileSyncState Get(Guid profileId)
    {
        if (!_states.TryGetValue(profileId, out var state))
        {
            state = new ProfileSyncState { ProfileId = profileId };
            _states[profileId] = state;
        }

        return state;
    }

    public void Save(ProfileSyncState state) => _states[state.ProfileId] = state;
}
