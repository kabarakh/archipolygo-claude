using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>
/// Loads/saves connection profiles and the sync state (last seen
/// item/hint progress) as JSON under %AppData%/Archipolygo.
/// </summary>
public class PersistenceService : IPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _appDataDirectory;
    private readonly string _profilesFilePath;
    private readonly string _syncStateDirectory;
    private readonly string _settingsFilePath;

    public PersistenceService()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _appDataDirectory = Path.Combine(baseDirectory, "Archipolygo");
        _profilesFilePath = Path.Combine(_appDataDirectory, "profiles.json");
        _syncStateDirectory = Path.Combine(_appDataDirectory, "sync-state");
        _settingsFilePath = Path.Combine(_appDataDirectory, "settings.json");

        Directory.CreateDirectory(_appDataDirectory);
        Directory.CreateDirectory(_syncStateDirectory);
    }

    public List<ServerProfile> LoadProfiles()
    {
        if (!File.Exists(_profilesFilePath))
        {
            return new List<ServerProfile>();
        }

        try
        {
            var json = File.ReadAllText(_profilesFilePath);
            return JsonSerializer.Deserialize<List<ServerProfile>>(json, JsonOptions) ?? new List<ServerProfile>();
        }
        catch (Exception)
        {
            // Corrupted/incompatible file: prefer an empty list over a crash at startup.
            return new List<ServerProfile>();
        }
    }

    public void SaveProfiles(IEnumerable<ServerProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles.ToList(), JsonOptions);
        File.WriteAllText(_profilesFilePath, json);
    }

    public ProfileSyncState LoadSyncState(Guid profileId)
    {
        var path = GetSyncStateFilePath(profileId);
        if (!File.Exists(path))
        {
            return new ProfileSyncState { ProfileId = profileId };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProfileSyncState>(json, JsonOptions)
                   ?? new ProfileSyncState { ProfileId = profileId };
        }
        catch (Exception)
        {
            return new ProfileSyncState { ProfileId = profileId };
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

    private string GetSyncStateFilePath(Guid profileId) =>
        Path.Combine(_syncStateDirectory, $"{profileId}.json");
}
