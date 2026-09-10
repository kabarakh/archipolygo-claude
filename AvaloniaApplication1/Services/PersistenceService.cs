using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Loads/saves server connection groups (and the sync state - last seen
/// item/hint progress - per slot) as JSON under %AppData%/Archipolygo.
/// </summary>
public class PersistenceService : IPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // ServerConnectionGroup.Slots is a get-only ObservableCollection
        // property (no setter, so the deserialized list's own slots can't
        // just be assigned wholesale - the existing GroupViewModel/UI
        // bindings need to keep observing the same collection instance).
        // Without this, System.Text.Json's default behaviour for a
        // get-only collection property is to serialize it fine but silently
        // skip populating it back on deserialize, i.e. every group would
        // come back from disk with zero slots and (since InitializeGroupAsync
        // requires Slots.Count > 0) never auto-connect either. Populate
        // fills the already-constructed collection in place instead of
        // trying to replace it.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    private readonly string _appDataDirectory;
    private readonly string _groupsFilePath;

    /// <summary>
    /// Pre-Phase-6 flat profile list ("one ServerProfile = one independent
    /// connection, Host+Port+SlotName+Password all on one record"). Only
    /// ever read once, by <see cref="MigrateLegacyProfilesIfNeeded"/>, to
    /// build the new group/slot shape the first time this version of the app
    /// runs against an older data directory.
    /// </summary>
    private readonly string _legacyProfilesFilePath;

    private readonly string _syncStateDirectory;
    private readonly string _settingsFilePath;
    private readonly string _dataPackageCacheDirectory;

    public PersistenceService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Archipolygo"))
    {
    }

    /// <summary>
    /// Test-only entry point (see AvaloniaApplication1.Tests'
    /// PersistenceServiceTests) that points every file this service reads/
    /// writes at <paramref name="appDataDirectory"/> instead of the real
    /// %AppData%/Archipolygo - so a round-trip test never touches the user's
    /// actual groups.json/sync-state/settings.json. The parameterless
    /// constructor above is what the real app always uses.
    /// </summary>
    internal PersistenceService(string appDataDirectory)
    {
        _appDataDirectory = appDataDirectory;
        _groupsFilePath = Path.Combine(_appDataDirectory, "groups.json");
        _legacyProfilesFilePath = Path.Combine(_appDataDirectory, "profiles.json");
        _syncStateDirectory = Path.Combine(_appDataDirectory, "sync-state");
        _settingsFilePath = Path.Combine(_appDataDirectory, "settings.json");
        _dataPackageCacheDirectory = Path.Combine(_appDataDirectory, "datapackage-cache");

        Directory.CreateDirectory(_appDataDirectory);
        Directory.CreateDirectory(_syncStateDirectory);
        // Not _dataPackageCacheDirectory itself - created lazily per group in
        // SaveDataPackageCache, since most groups never end up needing one
        // (Item mode in the Hint picker may never be opened for them).
    }

    public List<ServerConnectionGroup> LoadGroups()
    {
        if (!File.Exists(_groupsFilePath))
        {
            var migrated = MigrateLegacyProfilesIfNeeded();
            if (migrated is not null)
            {
                SaveGroups(migrated);
                return migrated;
            }

            return new List<ServerConnectionGroup>();
        }

        try
        {
            var json = File.ReadAllText(_groupsFilePath);
            return JsonSerializer.Deserialize<List<ServerConnectionGroup>>(json, JsonOptions) ?? new List<ServerConnectionGroup>();
        }
        catch (Exception)
        {
            // Corrupted/incompatible file: prefer an empty list over a crash at startup.
            return new List<ServerConnectionGroup>();
        }
    }

    public void SaveGroups(IEnumerable<ServerConnectionGroup> groups)
    {
        var json = JsonSerializer.Serialize(groups.ToList(), JsonOptions);
        File.WriteAllText(_groupsFilePath, json);
    }

    public ProfileSyncState LoadSyncState(Guid slotId)
    {
        var path = GetSyncStateFilePath(slotId);
        if (!File.Exists(path))
        {
            return new ProfileSyncState { ProfileId = slotId };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProfileSyncState>(json, JsonOptions)
                   ?? new ProfileSyncState { ProfileId = slotId };
        }
        catch (Exception)
        {
            return new ProfileSyncState { ProfileId = slotId };
        }
    }

    public void SaveSyncState(ProfileSyncState state)
    {
        var path = GetSyncStateFilePath(state.ProfileId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(path, json);
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public DataPackageCacheEntry? LoadDataPackageCache(Guid groupId, string game)
    {
        var path = GetDataPackageCacheFilePath(groupId, game);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DataPackageCacheEntry>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SaveDataPackageCache(Guid groupId, string game, DataPackageCacheEntry entry)
    {
        var path = GetDataPackageCacheFilePath(groupId, game);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void DeleteDataPackageCacheForGroup(Guid groupId)
    {
        var groupDirectory = Path.Combine(_dataPackageCacheDirectory, groupId.ToString());
        try
        {
            if (Directory.Exists(groupDirectory))
            {
                Directory.Delete(groupDirectory, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup - an orphaned cache folder is harmless
            // clutter, not worth failing the actual group removal over.
        }
    }

    /// <summary>
    /// One file per (group, game) rather than one shared file per group, so a
    /// single corrupted/partially-written entry (e.g. app killed mid-write)
    /// can't take down every other cached game for that same server -
    /// consistent with <see cref="LoadDataPackageCache"/>/<see cref="LoadSyncState"/>
    /// both tolerating a corrupt file by falling back rather than throwing.
    /// <paramref name="game"/> is sanitized since it's server-controlled,
    /// arbitrary text (an Archipelago game name), not something this app
    /// generates itself the way a <see cref="Guid"/> is.
    /// </summary>
    private string GetDataPackageCacheFilePath(Guid groupId, string game) =>
        Path.Combine(_dataPackageCacheDirectory, groupId.ToString(), $"{SanitizeFileName(game)}.json");

    private static string SanitizeFileName(string name)
    {
        var sanitized = name;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized;
    }

    /// <summary>
    /// One-time migration from the pre-Phase-6 flat profile list to the new
    /// group/slot shape: profiles sharing the same Host+Port (case-insensitive
    /// host) become one <see cref="ServerConnectionGroup"/>, each keeping its
    /// original <c>Id</c> as its new <see cref="SlotProfile.Id"/> so the
    /// already-persisted <see cref="ProfileSyncState"/> per profile (last
    /// seen item index, seen hint ids) still applies without any changes.
    /// The group takes the Name/Password of the first profile in each
    /// Host+Port bucket (order as read from the old file); if profiles in the
    /// same bucket happened to have different passwords, the others are
    /// silently dropped in favour of the first - room passwords realistically
    /// don't differ per slot on the same server. Returns null if there is no
    /// legacy file to migrate (fresh install, or already migrated before).
    /// </summary>
    private List<ServerConnectionGroup>? MigrateLegacyProfilesIfNeeded()
    {
        if (!File.Exists(_legacyProfilesFilePath))
        {
            return null;
        }

        List<LegacyServerProfile>? legacyProfiles;
        try
        {
            var json = File.ReadAllText(_legacyProfilesFilePath);
            legacyProfiles = JsonSerializer.Deserialize<List<LegacyServerProfile>>(json, JsonOptions);
        }
        catch (Exception)
        {
            legacyProfiles = null;
        }

        if (legacyProfiles is null || legacyProfiles.Count == 0)
        {
            return new List<ServerConnectionGroup>();
        }

        var groups = new List<ServerConnectionGroup>();

        foreach (var bucket in legacyProfiles.GroupBy(p => (Host: p.Host.Trim().ToLowerInvariant(), p.Port)))
        {
            var first = bucket.First();
            var group = new ServerConnectionGroup
            {
                Id = Guid.NewGuid(),
                Name = first.Name,
                Host = first.Host,
                Port = first.Port,
                Password = first.Password
            };

            foreach (var legacy in bucket)
            {
                var slot = new SlotProfile
                {
                    Id = legacy.Id,
                    GroupId = group.Id,
                    SlotName = legacy.SlotName
                };
                group.Slots.Add(slot);

                if (legacy.AutoConnect && group.PreferredLeaderSlotId is null)
                {
                    group.AutoConnect = true;
                    group.PreferredLeaderSlotId = slot.Id;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private string GetSyncStateFilePath(Guid slotId) =>
        Path.Combine(_syncStateDirectory, $"{slotId}.json");

    /// <summary>Shape of the pre-Phase-6 <c>profiles.json</c> file, kept only for one-time migration.</summary>
    private sealed class LegacyServerProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string SlotName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool AutoConnect { get; set; }
    }
}
