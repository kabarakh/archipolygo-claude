using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// On-disk cache of one game's DataPackage names, valid while <see cref="Checksum"/> still matches the room's
/// <c>datapackage_checksums</c> entry. Scoped per group (not global) so removing a server can delete its own cache folder.
/// </summary>
public class DataPackageCacheEntry
{
    public string Checksum { get; set; } = string.Empty;

    public List<string> ItemNames { get; set; } = new();

    /// <summary>Location name -> id. Null in older cache files, which is treated as a cache miss and refetched.</summary>
    public Dictionary<string, long>? LocationIds { get; set; }
}
