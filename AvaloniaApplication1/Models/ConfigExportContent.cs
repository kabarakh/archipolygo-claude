using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>What a config export file holds. <see cref="Settings"/> is null if the export left the app settings out.</summary>
public sealed record ConfigExportContent(IReadOnlyList<ServerConnectionGroup> Groups, AppSettings? Settings);
