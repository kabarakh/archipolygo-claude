using System;
using System.Collections.Generic;
using Archipolygo.Models;
using Archipolygo.Services;

namespace Archipolygo.ViewModels;

/// <summary>What the Settings dialog's data section needs from the main window: export, import and the data folder.</summary>
/// <param name="StartImport">Returns at once; reconnecting imported servers continues in the background.</param>
public sealed record DataManagementContext(
    DataDirectory DataDirectory,
    IConfigExportService ConfigExportService,
    Func<IReadOnlyList<ServerConnectionGroup>> GetGroups,
    Func<AppSettings> LoadSettings,
    Action<ConfigImportSelection> StartImport);
