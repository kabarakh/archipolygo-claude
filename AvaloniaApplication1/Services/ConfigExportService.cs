using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Archipolygo.Models;

namespace Archipolygo.Services;

public class ConfigExportService : IConfigExportService
{
    /// <summary>Bump only for a change an older app can't read; additive group/settings fields don't need it.</summary>
    internal const int CurrentFormatVersion = 1;

    internal const string FormatVersionProperty = "FormatVersion";
    internal const string GroupsProperty = "Groups";
    internal const string SettingsProperty = "Settings";

    public void Export(string filePath, IEnumerable<ServerConnectionGroup> groups, AppSettings? settings)
    {
        var root = new JsonObject
        {
            [FormatVersionProperty] = CurrentFormatVersion,
            [GroupsProperty] = JsonSerializer.SerializeToNode(groups.ToList(), PersistenceService.JsonOptions)
        };

        if (settings is not null)
        {
            root[SettingsProperty] = JsonSerializer.SerializeToNode(settings, PersistenceService.JsonOptions);
        }

        File.WriteAllText(filePath, root.ToJsonString(PersistenceService.JsonOptions));
    }

    public ConfigExportContent Import(string filePath) => ImportFromJson(File.ReadAllText(filePath));

    public ConfigExportContent ImportFromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            EnsureKnownFormatVersion(root);

            if (!root.TryGetProperty(GroupsProperty, out var groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ConfigImportException("The file contains no server list.");
            }

            var groups = PersistenceService.DeserializeGroups(groupsElement.GetRawText());

            AppSettings? settings = null;
            if (root.TryGetProperty(SettingsProperty, out var settingsElement) && settingsElement.ValueKind == JsonValueKind.Object)
            {
                settings = settingsElement.Deserialize<AppSettings>(PersistenceService.JsonOptions);
            }

            return new ConfigExportContent(groups, settings);
        }
        catch (JsonException ex)
        {
            throw new ConfigImportException("The file is not a valid Archipolygo export.", ex);
        }
    }

    private static void EnsureKnownFormatVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(FormatVersionProperty, out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version) ||
            version < 1)
        {
            throw new ConfigImportException("The file is not an Archipolygo export.");
        }

        if (version > CurrentFormatVersion)
        {
            throw new ConfigImportException(
                $"The file was exported by a newer Archipolygo version (format {version}). Update Archipolygo to import it.");
        }
    }
}
