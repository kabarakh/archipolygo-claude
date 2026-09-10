using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): reine Datei-Roundtrip-Logik, kein
/// Avalonia nötig. Jeder Test bekommt sein eigenes Temp-Verzeichnis über die
/// internal-only <c>PersistenceService(string appDataDirectory)</c>-Konstruktor
/// (siehe deren Doc-Comment) - niemals das echte %AppData%/Archipolygo.
/// </summary>
public sealed class PersistenceServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _service;

    public PersistenceServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        _service = new PersistenceService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoadGroups_RoundTripsSlots()
    {
        // Regression test for the PreferredObjectCreationHandling gotcha
        // documented in CLAUDE.md: without it, Slots (a get-only
        // ObservableCollection<SlotProfile> property) silently comes back
        // empty from disk with no exception.
        var group = new ServerConnectionGroup
        {
            Name = "My Server",
            Host = "archipelago.gg",
            Port = 12345,
            Password = "secret",
            AutoConnect = true
        };
        var slot1 = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        var slot2 = new SlotProfile { GroupId = group.Id, SlotName = "Bob", Password = "override" };
        group.Slots.Add(slot1);
        group.Slots.Add(slot2);
        group.PreferredLeaderSlotId = slot1.Id;

        _service.SaveGroups(new[] { group });
        var loaded = _service.LoadGroups();

        var loadedGroup = Assert.Single(loaded);
        Assert.Equal(group.Id, loadedGroup.Id);
        Assert.Equal("My Server", loadedGroup.Name);
        Assert.Equal("archipelago.gg", loadedGroup.Host);
        Assert.Equal(12345, loadedGroup.Port);
        Assert.Equal("secret", loadedGroup.Password);
        Assert.True(loadedGroup.AutoConnect);
        Assert.Equal(slot1.Id, loadedGroup.PreferredLeaderSlotId);

        Assert.Equal(2, loadedGroup.Slots.Count);
        var loadedSlot1 = loadedGroup.Slots.Single(s => s.Id == slot1.Id);
        Assert.Equal("Alice", loadedSlot1.SlotName);
        Assert.Null(loadedSlot1.Password);
        var loadedSlot2 = loadedGroup.Slots.Single(s => s.Id == slot2.Id);
        Assert.Equal("Bob", loadedSlot2.SlotName);
        Assert.Equal("override", loadedSlot2.Password);
    }

    [Fact]
    public void LoadGroups_NoGroupsFileAndNoLegacyFile_ReturnsEmptyList()
    {
        var loaded = _service.LoadGroups();

        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadGroups_MigratesLegacyProfilesWithSameHostAndPort_IntoOneGroup()
    {
        // Old flat ServerProfile shape (pre-Phase-6) - see PersistenceService's
        // private LegacyServerProfile. Two profiles share Host+Port and should
        // merge into one ServerConnectionGroup; a third, on a different port,
        // should become its own group.
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var carolId = Guid.NewGuid();

        var legacyProfiles = new object[]
        {
            new { Id = aliceId, Name = "Room A", Host = "archipelago.gg", Port = 38281, SlotName = "Alice", Password = "pw", AutoConnect = true },
            new { Id = bobId, Name = "Room A (dup)", Host = "archipelago.gg", Port = 38281, SlotName = "Bob", Password = "pw-ignored", AutoConnect = false },
            new { Id = carolId, Name = "Room B", Host = "archipelago.gg", Port = 38282, SlotName = "Carol", Password = "pw2", AutoConnect = false },
        };
        WriteLegacyProfilesFile(legacyProfiles);

        var loaded = _service.LoadGroups();

        Assert.Equal(2, loaded.Count);

        var roomA = loaded.Single(g => g.Slots.Any(s => s.Id == aliceId));
        Assert.Equal(2, roomA.Slots.Count);
        Assert.Contains(roomA.Slots, s => s.Id == aliceId && s.SlotName == "Alice");
        Assert.Contains(roomA.Slots, s => s.Id == bobId && s.SlotName == "Bob");
        // First profile in the Host+Port bucket wins the group-level password/name/AutoConnect.
        Assert.Equal("pw", roomA.Password);
        Assert.True(roomA.AutoConnect);
        Assert.Equal(aliceId, roomA.PreferredLeaderSlotId);

        var roomB = loaded.Single(g => g.Slots.Any(s => s.Id == carolId));
        Assert.Single(roomB.Slots);
        Assert.Equal("Carol", roomB.Slots[0].SlotName);

        // Migration also persists the new-shape groups.json so this only ever runs once.
        Assert.True(File.Exists(Path.Combine(_tempDirectory, "groups.json")));
    }

    [Fact]
    public void SaveThenLoadSyncState_RoundTripsLastSeenItemIndexAndSeenHintIds()
    {
        var slotId = Guid.NewGuid();
        var state = new ProfileSyncState
        {
            ProfileId = slotId,
            LastSeenItemIndex = 42,
            SeenHintIds = { "hint-1", "hint-2" }
        };

        _service.SaveSyncState(state);
        var loaded = _service.LoadSyncState(slotId);

        Assert.Equal(slotId, loaded.ProfileId);
        Assert.Equal(42, loaded.LastSeenItemIndex);
        Assert.Equal(new[] { "hint-1", "hint-2" }, loaded.SeenHintIds.OrderBy(x => x));
    }

    [Fact]
    public void LoadSyncState_NoFileYet_ReturnsFreshStateForThatSlot()
    {
        var slotId = Guid.NewGuid();

        var loaded = _service.LoadSyncState(slotId);

        Assert.Equal(slotId, loaded.ProfileId);
        Assert.Equal(0, loaded.LastSeenItemIndex);
        Assert.Empty(loaded.SeenHintIds);
    }

    [Fact]
    public void SaveThenLoadDataPackageCache_RoundTripsChecksumAndItemNames()
    {
        var groupId = Guid.NewGuid();
        var entry = new DataPackageCacheEntry { Checksum = "abc123", ItemNames = { "Fire Rod", "Ice Rod" } };

        _service.SaveDataPackageCache(groupId, "A Link to the Past", entry);
        var loaded = _service.LoadDataPackageCache(groupId, "A Link to the Past");

        Assert.NotNull(loaded);
        Assert.Equal("abc123", loaded!.Checksum);
        Assert.Equal(new[] { "Fire Rod", "Ice Rod" }, loaded.ItemNames);
    }

    [Fact]
    public void LoadDataPackageCache_NothingCachedYet_ReturnsNull()
    {
        var loaded = _service.LoadDataPackageCache(Guid.NewGuid(), "Some Game");

        Assert.Null(loaded);
    }

    [Fact]
    public void SaveDataPackageCache_GameNameWithInvalidFileNameCharacters_StillRoundTrips()
    {
        var groupId = Guid.NewGuid();
        var entry = new DataPackageCacheEntry { Checksum = "xyz", ItemNames = { "Widget" } };

        _service.SaveDataPackageCache(groupId, "Some/Game: Weird*Name?", entry);
        var loaded = _service.LoadDataPackageCache(groupId, "Some/Game: Weird*Name?");

        Assert.NotNull(loaded);
        Assert.Equal("xyz", loaded!.Checksum);
    }

    [Fact]
    public void DeleteDataPackageCacheForGroup_RemovesEveryGameCachedForThatGroup_LeavesOtherGroupsAlone()
    {
        var groupToRemove = Guid.NewGuid();
        var otherGroup = Guid.NewGuid();
        _service.SaveDataPackageCache(groupToRemove, "Game A", new DataPackageCacheEntry { Checksum = "1" });
        _service.SaveDataPackageCache(groupToRemove, "Game B", new DataPackageCacheEntry { Checksum = "2" });
        _service.SaveDataPackageCache(otherGroup, "Game A", new DataPackageCacheEntry { Checksum = "3" });

        _service.DeleteDataPackageCacheForGroup(groupToRemove);

        Assert.Null(_service.LoadDataPackageCache(groupToRemove, "Game A"));
        Assert.Null(_service.LoadDataPackageCache(groupToRemove, "Game B"));
        Assert.NotNull(_service.LoadDataPackageCache(otherGroup, "Game A"));
    }

    [Fact]
    public void DeleteDataPackageCacheForGroup_NothingEverCached_DoesNotThrow()
    {
        _service.DeleteDataPackageCacheForGroup(Guid.NewGuid());
    }

    private void WriteLegacyProfilesFile(object legacyProfiles)
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "profiles.json");
        File.WriteAllText(path, JsonSerializer.Serialize(legacyProfiles));
    }
}
