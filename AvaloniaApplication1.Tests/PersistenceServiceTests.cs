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
        var slot1 = new SlotProfile { GroupId = group.Id, SlotName = "Alice", RequiresPassword = true };
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
        // Feature-Plaene/Passwort-Speicherung.md: never persisted - see the
        // two tests below for the actual password-handling behavior.
        Assert.Equal(string.Empty, loadedGroup.Password);
        Assert.True(loadedGroup.AutoConnect);
        Assert.Equal(slot1.Id, loadedGroup.PreferredLeaderSlotId);

        Assert.Equal(2, loadedGroup.Slots.Count);
        var loadedSlot1 = loadedGroup.Slots.Single(s => s.Id == slot1.Id);
        Assert.Equal("Alice", loadedSlot1.SlotName);
        Assert.Null(loadedSlot1.Password);
        Assert.True(loadedSlot1.RequiresPassword);
        var loadedSlot2 = loadedGroup.Slots.Single(s => s.Id == slot2.Id);
        Assert.Equal("Bob", loadedSlot2.SlotName);
        Assert.Null(loadedSlot2.Password);
        Assert.False(loadedSlot2.RequiresPassword);
    }

    [Fact]
    public void SaveGroups_NeverWritesAPasswordKey_EvenWhenOneIsSet()
    {
        // The actual "don't store passwords" promise, checked against the
        // raw file text rather than the typed model - a regression here
        // (e.g. someone removing [JsonIgnore]) wouldn't be caught by the
        // round-trip test above, since that only asserts what comes back
        // out, not what's actually sitting on disk in between.
        var group = new ServerConnectionGroup { Name = "S", Host = "h", Port = 1, Password = "secret" };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice", Password = "slot-secret" };
        group.Slots.Add(slot);

        _service.SaveGroups(new[] { group });

        var rawJson = File.ReadAllText(Path.Combine(_tempDirectory, "groups.json"));
        Assert.DoesNotContain("secret", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Password\"", rawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadGroups_LegacyFileWithRealPassword_MigratesToRequiresPasswordFlag()
    {
        // Simulates a groups.json written by a pre-Passwort-Speicherung.md
        // build: a real "Password" string still on disk. The normal typed
        // Deserialize silently drops it (now [JsonIgnore]d) - this is what
        // ApplyLegacyPasswordRequirement's raw pre-pass is for for.
        Directory.CreateDirectory(_tempDirectory);
        var groupId = Guid.NewGuid();
        var slotWithGroupPasswordId = Guid.NewGuid();
        var slotWithOwnOverrideId = Guid.NewGuid();
        var slotNeverPasswordedId = Guid.NewGuid();
        var legacyJson = $$"""
            [
              {
                "Id": "{{groupId}}",
                "Name": "Legacy Server",
                "Host": "archipelago.gg",
                "Port": 38281,
                "Password": "secret",
                "AutoConnect": true,
                "Slots": [
                  { "Id": "{{slotWithGroupPasswordId}}", "GroupId": "{{groupId}}", "SlotName": "Alice", "Password": null },
                  { "Id": "{{slotWithOwnOverrideId}}", "GroupId": "{{groupId}}", "SlotName": "Bob", "Password": "own-override" },
                  { "Id": "{{slotNeverPasswordedId}}", "GroupId": "{{groupId}}", "SlotName": "Carol", "Password": "" }
                ]
              }
            ]
            """;
        File.WriteAllText(Path.Combine(_tempDirectory, "groups.json"), legacyJson);

        var loaded = _service.LoadGroups();

        var loadedGroup = Assert.Single(loaded);
        Assert.Equal(string.Empty, loadedGroup.Password); // never re-populated from the legacy text
        Assert.All(loadedGroup.Slots, s => Assert.True(s.RequiresPassword));
        Assert.All(loadedGroup.Slots, s => Assert.Null(s.Password));
    }

    [Fact]
    public void LoadGroups_GroupsFileWithoutColorField_AssignsDeterministicColorsInFileOrder()
    {
        // Feature-Plaene/Server-Farben.md's migration: a groups.json written
        // before ServerConnectionGroup.Color existed has no "Color" field at
        // all - the normal typed Deserialize just leaves it at its default
        // (empty string), and AssignMissingColors is what's supposed to fill
        // it in, in file order, exactly as if each group had been created
        // fresh one after another.
        Directory.CreateDirectory(_tempDirectory);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var legacyJson = $$"""
            [
              { "Id": "{{firstId}}", "Name": "First", "Host": "archipelago.gg", "Port": 1, "Slots": [] },
              { "Id": "{{secondId}}", "Name": "Second", "Host": "archipelago.gg", "Port": 2, "Slots": [] },
              { "Id": "{{thirdId}}", "Name": "Third", "Host": "archipelago.gg", "Port": 3, "Slots": [] }
            ]
            """;
        File.WriteAllText(Path.Combine(_tempDirectory, "groups.json"), legacyJson);

        var loaded = _service.LoadGroups();

        Assert.Equal(ServerColorPalette.Colors[0], loaded.Single(g => g.Id == firstId).Color);
        Assert.Equal(ServerColorPalette.Colors[1], loaded.Single(g => g.Id == secondId).Color);
        Assert.Equal(ServerColorPalette.Colors[2], loaded.Single(g => g.Id == thirdId).Color);
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

    [Fact]
    public void SaveThenLoadSettings_RoundTripsThemePreference()
    {
        var settings = new AppSettings { ThemePreference = "Dark" };

        _service.SaveSettings(settings);
        var loaded = _service.LoadSettings();

        Assert.Equal("Dark", loaded.ThemePreference);
    }

    [Fact]
    public void LoadSettings_FileFromBeforeThemePreferenceExisted_DefaultsToSystem()
    {
        // Simulates an upgrade from an older install: a settings.json
        // written before ThemePreference existed at all, not just one where
        // it happens to be unset.
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { DefaultAutoConnect = true, EventHistoryLimit = 500 }));

        var loaded = _service.LoadSettings();

        Assert.Equal("System", loaded.ThemePreference);
        Assert.True(loaded.DefaultAutoConnect);
    }

    private void WriteLegacyProfilesFile(object legacyProfiles)
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "profiles.json");
        File.WriteAllText(path, JsonSerializer.Serialize(legacyProfiles));
    }
}
