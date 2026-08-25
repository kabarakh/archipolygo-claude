using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;

namespace TestHarness;

/// <summary>
/// In-memory stand-in for <see cref="IPersistenceService"/> - see
/// .claude/skills/app-testen. Never touches the real
/// %AppData%/Archipolygo files; starts with zero groups and silently
/// discards every save.
/// </summary>
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
}
