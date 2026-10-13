using System;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// One shared <see cref="ProfileSyncState"/> instance per profile. Hint and item services write
/// different fields of the same file; separate copies would overwrite each other's fields (lost update).
/// </summary>
public interface IProfileSyncStateStore
{
    ProfileSyncState Get(Guid profileId);

    void Save(ProfileSyncState state);
}
