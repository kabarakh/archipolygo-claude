using System;
using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

public interface IPersistenceService
{
    List<ServerProfile> LoadProfiles();

    void SaveProfiles(IEnumerable<ServerProfile> profiles);

    ProfileSyncState LoadSyncState(Guid profileId);

    void SaveSyncState(ProfileSyncState state);

    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);
}
