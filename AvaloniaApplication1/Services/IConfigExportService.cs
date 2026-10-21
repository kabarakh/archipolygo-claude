using System.Collections.Generic;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>Reads and writes the single-file server/settings export. Sync state, data-package cache and logs are never part of it.</summary>
public interface IConfigExportService
{
    /// <param name="settings">Null leaves the app settings out of the file.</param>
    void Export(string filePath, IEnumerable<ServerConnectionGroup> groups, AppSettings? settings);

    /// <summary>Groups go through the same upgrades as <see cref="IPersistenceService.LoadGroups"/>.</summary>
    /// <exception cref="ConfigImportException">Malformed file, not an export, or a format version this app doesn't know.</exception>
    ConfigExportContent Import(string filePath);

    /// <inheritdoc cref="Import"/>
    ConfigExportContent ImportFromJson(string json);
}
