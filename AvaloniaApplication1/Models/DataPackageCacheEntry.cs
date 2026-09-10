using System.Collections.Generic;

namespace Archipolygo.Models;

/// <summary>
/// On-disk cache of one game's full item-name vocabulary (the server's
/// DataPackage <c>item_name_to_id</c> table for that game - see
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md), so the "Hint..." picker's Item
/// mode doesn't have to re-request the whole DataPackage every time it opens.
/// <see cref="Checksum"/> is the game's <c>RoomInfo.datapackage_checksums</c>
/// entry at the time this was cached - a later connect whose own checksum for
/// this game still matches can skip the re-fetch entirely and just use
/// <see cref="ItemNames"/> as-is; a changed checksum means the game's item
/// data itself changed (a new Archipelago release, a modified apworld, ...)
/// and this entry is stale. Scoped per <see cref="ServerConnectionGroup"/>
/// (see <see cref="Services.IPersistenceService.SaveDataPackageCache"/>)
/// rather than shared globally across every configured server, purely so
/// removing a server can simply delete its own cache folder - at the minor
/// cost of re-fetching once per group for a game two different rooms happen
/// to share.
/// </summary>
public class DataPackageCacheEntry
{
    public string Checksum { get; set; } = string.Empty;

    public List<string> ItemNames { get; set; } = new();
}
