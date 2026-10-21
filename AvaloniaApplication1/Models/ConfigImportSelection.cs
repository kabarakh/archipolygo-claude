using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>What the user picked in the import dialog. <see cref="Settings"/> is null if they left the app settings out.</summary>
public sealed record ConfigImportSelection(IReadOnlyList<ServerConnectionGroup> Groups, AppSettings? Settings);
