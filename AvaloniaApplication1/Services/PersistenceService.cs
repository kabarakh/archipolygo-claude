using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>Loads/saves groups, per-slot sync state and settings as JSON under %AppData%/Archipolygo.</summary>
public class PersistenceService : IPersistenceService
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,

        // Without Populate, System.Text.Json silently skips get-only collections like ServerConnectionGroup.Slots on deserialize.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    private readonly string _appDataDirectory;
    private readonly string _groupsFilePath;
    private readonly string _legacyProfilesFilePath;

    private readonly string _syncStateDirectory;
    private readonly string _settingsFilePath;
    private readonly string _dataPackageCacheDirectory;
    private readonly IDiagnosticLogger _diagnosticLogger;

    public PersistenceService(IDiagnosticLogger? diagnosticLogger = null)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Archipolygo"), diagnosticLogger)
    {
    }

    /// <summary>Test-only: redirects all files to <paramref name="appDataDirectory"/>.</summary>
    internal PersistenceService(string appDataDirectory, IDiagnosticLogger? diagnosticLogger = null)
    {
        _appDataDirectory = appDataDirectory;
        _diagnosticLogger = diagnosticLogger ?? NullDiagnosticLogger.Instance;
        _groupsFilePath = Path.Combine(_appDataDirectory, "groups.json");
        _legacyProfilesFilePath = Path.Combine(_appDataDirectory, "profiles.json");
        _syncStateDirectory = Path.Combine(_appDataDirectory, "sync-state");
        _settingsFilePath = Path.Combine(_appDataDirectory, "settings.json");
        _dataPackageCacheDirectory = Path.Combine(_appDataDirectory, "datapackage-cache");

        Directory.CreateDirectory(_appDataDirectory);
        Directory.CreateDirectory(_syncStateDirectory);
        // The data-package cache directory is created lazily; most groups never need one.
    }

    public List<ServerConnectionGroup> LoadGroups()
    {
        if (!File.Exists(_groupsFilePath))
        {
            var migrated = MigrateLegacyProfilesIfNeeded();
            if (migrated is not null)
            {
                AssignMissingColors(migrated);
                SaveGroups(migrated);
                return migrated;
            }

            return new List<ServerConnectionGroup>();
        }

        try
        {
            return DeserializeGroups(File.ReadAllText(_groupsFilePath));
        }
        catch (Exception ex)
        {
            // Corrupted/incompatible file: prefer an empty list over a crash at startup.
            _diagnosticLogger.Error($"Failed to load {_groupsFilePath}, starting with no groups", ex);
            return new List<ServerConnectionGroup>();
        }
    }

    /// <summary>
    /// Parses a <c>groups.json</c>-shaped JSON array and applies the same upgrades as <see cref="LoadGroups"/>.
    /// Throws <see cref="JsonException"/> on malformed input; the caller decides how to recover.
    /// </summary>
    internal static List<ServerConnectionGroup> DeserializeGroups(string json)
    {
        var groups = JsonSerializer.Deserialize<List<ServerConnectionGroup>>(json, JsonOptions) ?? new List<ServerConnectionGroup>();
        ApplyLegacyPasswordRequirement(json, groups);
        AssignMissingColors(groups);
        return groups;
    }

    /// <summary>Assigns colors in file order, giving the same result as if each group had been created fresh one after another.</summary>
    private static void AssignMissingColors(List<ServerConnectionGroup> groups)
    {
        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Color))
            {
                group.Color = ServerColorPalette.AssignColor(groups.Select(g => g.Color));
            }
        }
    }

    /// <summary>
    /// Passwords are <c>[JsonIgnore]</c>d now, so an old file's "Password" text is only visible via a raw JSON walk;
    /// a non-empty one sets <see cref="SlotProfile.RequiresPassword"/>. Becomes a no-op once the file is saved again.
    /// Matched by position, since both lists come from the same JSON text.
    /// </summary>
    private static void ApplyLegacyPasswordRequirement(string json, List<ServerConnectionGroup> groups)
    {
        if (groups.Count == 0)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var groupIndex = 0;
            foreach (var groupElement in document.RootElement.EnumerateArray())
            {
                if (groupIndex >= groups.Count)
                {
                    break;
                }

                var group = groups[groupIndex];
                groupIndex++;

                var groupHadPassword = HasNonEmptyLegacyPassword(groupElement);

                if (!groupElement.TryGetProperty("Slots", out var slotsElement) || slotsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var slotIndex = 0;
                foreach (var slotElement in slotsElement.EnumerateArray())
                {
                    if (slotIndex >= group.Slots.Count)
                    {
                        break;
                    }

                    // Either the slot override or the group's shared password could have been the one used.
                    if (groupHadPassword || HasNonEmptyLegacyPassword(slotElement))
                    {
                        group.Slots[slotIndex].RequiresPassword = true;
                    }

                    slotIndex++;
                }
            }
        }
        catch (Exception)
        {
            // Best-effort; the typed load already succeeded.
        }
    }

    private static bool HasNonEmptyLegacyPassword(JsonElement element) =>
        element.TryGetProperty("Password", out var passwordElement) &&
        passwordElement.ValueKind == JsonValueKind.String &&
        !string.IsNullOrEmpty(passwordElement.GetString());

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
        catch (Exception ex)
        {
            _diagnosticLogger.Error($"Failed to load sync state {path} for slot {slotId}, treating as never synced", ex);
            return new ProfileSyncState { ProfileId = slotId };
        }
    }

    public void SaveSyncState(ProfileSyncState state)
    {
        var path = GetSyncStateFilePath(state.ProfileId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void DeleteSyncState(Guid slotId)
    {
        var path = GetSyncStateFilePath(slotId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
        catch (Exception ex)
        {
            _diagnosticLogger.Error($"Failed to load {_settingsFilePath}, falling back to defaults", ex);
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
            // Best-effort; an orphaned cache folder is harmless.
        }
    }

    /// <summary>
    /// One file per (group, game) so one corrupt entry can't take down the others.
    /// <paramref name="game"/> is server-controlled text, hence sanitized.
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
    /// Migrates the old flat profiles.json into groups by Host+Port. Old profile Ids become the
    /// <see cref="SlotProfile.Id"/>s so existing per-slot sync state still applies. Null if there is no legacy file.
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
            _diagnosticLogger.Warning($"Failed to read legacy {_legacyProfilesFilePath} for one-time migration, skipping it");
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

            var groupHadPassword = !string.IsNullOrEmpty(first.Password);

            foreach (var legacy in bucket)
            {
                var slot = new SlotProfile
                {
                    Id = legacy.Id,
                    GroupId = group.Id,
                    SlotName = legacy.SlotName,
                    // Passwords aren't persisted anymore; keep only the fact that one was needed.
                    RequiresPassword = groupHadPassword || !string.IsNullOrEmpty(legacy.Password)
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

    /// <summary>Shape of the legacy <c>profiles.json</c>, kept only for migration.</summary>
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
