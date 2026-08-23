using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

public interface IPersistenceService
{
    /// <summary>
    /// Loads every configured <see cref="ServerConnectionGroup"/> (each with
    /// its <see cref="ServerConnectionGroup.Slots"/> already populated). If
    /// no groups have ever been saved but a pre-Phase-6 <c>profiles.json</c>
    /// exists, it is migrated once into the new group/slot shape (grouping
    /// the old flat <c>ServerProfile</c> list by Host+Port) and immediately
    /// persisted in the new format - see <see cref="PersistenceService"/>.
    /// </summary>
    List<ServerConnectionGroup> LoadGroups();

    void SaveGroups(IEnumerable<ServerConnectionGroup> groups);

    ProfileSyncState LoadSyncState(Guid slotId);

    void SaveSyncState(ProfileSyncState state);

    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);
}
