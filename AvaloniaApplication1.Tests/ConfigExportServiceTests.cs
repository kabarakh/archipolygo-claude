using System;
using System.IO;
using System.Text.Json.Nodes;
using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

public sealed class ConfigExportServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _exportFilePath;
    private readonly ConfigExportService _service = new();

    public ConfigExportServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _exportFilePath = Path.Combine(_tempDirectory, "export.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static ServerConnectionGroup NewGroup()
    {
        var group = new ServerConnectionGroup
        {
            Name = "My Server",
            Host = "archipelago.gg",
            Port = 12345,
            Password = "secret",
            AutoConnect = true,
            IsAdmin = true,
            NotificationsMuted = true,
            Color = "Teal",
            TrackerId = "abc"
        };
        var alice = new SlotProfile { GroupId = group.Id, SlotName = "Alice", RequiresPassword = true };
        var bob = new SlotProfile { GroupId = group.Id, SlotName = "Bob" };
        group.Slots.Add(alice);
        group.Slots.Add(bob);
        group.PreferredLeaderSlotId = bob.Id;
        return group;
    }

    [Fact]
    public void ExportThenImport_RoundTripsGroupsAndSettings()
    {
        var group = NewGroup();
        var settings = new AppSettings { ThemePreference = "Dark", EventHistoryLimit = 500 };

        _service.Export(_exportFilePath, new[] { group }, settings);
        var imported = _service.Import(_exportFilePath);

        var importedGroup = Assert.Single(imported.Groups);
        Assert.Equal(group.Id, importedGroup.Id);
        Assert.Equal("My Server", importedGroup.Name);
        Assert.Equal("archipelago.gg", importedGroup.Host);
        Assert.Equal(12345, importedGroup.Port);
        Assert.True(importedGroup.AutoConnect);
        Assert.True(importedGroup.IsAdmin);
        Assert.True(importedGroup.NotificationsMuted);
        Assert.Equal("Teal", importedGroup.Color);
        Assert.Equal("abc", importedGroup.TrackerId);
        Assert.Equal(group.PreferredLeaderSlotId, importedGroup.PreferredLeaderSlotId);
        Assert.Collection(importedGroup.Slots,
            slot =>
            {
                Assert.Equal(group.Slots[0].Id, slot.Id);
                Assert.Equal("Alice", slot.SlotName);
                Assert.True(slot.RequiresPassword);
            },
            slot =>
            {
                Assert.Equal(group.Slots[1].Id, slot.Id);
                Assert.Equal("Bob", slot.SlotName);
                Assert.False(slot.RequiresPassword);
            });

        Assert.NotNull(imported.Settings);
        Assert.Equal("Dark", imported.Settings!.ThemePreference);
        Assert.Equal(500, imported.Settings.EventHistoryLimit);
    }

    [Fact]
    public void Export_NeverWritesPasswords()
    {
        _service.Export(_exportFilePath, new[] { NewGroup() }, settings: null);

        Assert.DoesNotContain("secret", File.ReadAllText(_exportFilePath));
    }

    [Fact]
    public void ExportWithoutSettings_ImportsNullSettings()
    {
        _service.Export(_exportFilePath, new[] { NewGroup() }, settings: null);
        var imported = _service.Import(_exportFilePath);

        Assert.Single(imported.Groups);
        Assert.Null(imported.Settings);
    }

    [Fact]
    public void ImportedGroups_SurviveSaveAndLoadThroughPersistenceService()
    {
        var group = NewGroup();
        _service.Export(_exportFilePath, new[] { group }, settings: null);
        var persistence = new PersistenceService(Path.Combine(_tempDirectory, "data"));

        persistence.SaveGroups(_service.Import(_exportFilePath).Groups);
        var loaded = Assert.Single(persistence.LoadGroups());

        Assert.Equal(group.Id, loaded.Id);
        Assert.Equal(2, loaded.Slots.Count);
        Assert.True(loaded.AutoConnect);
    }

    [Fact]
    public void Import_OlderGroupShape_GoesThroughGroupsJsonMigration()
    {
        // Groups as an older app wrote them: plain-text passwords and no color yet.
        File.WriteAllText(_exportFilePath, """
            {
              "FormatVersion": 1,
              "Groups": [
                {
                  "Name": "Old",
                  "Host": "localhost",
                  "Port": 38281,
                  "Password": "",
                  "Slots": [
                    { "SlotName": "Alice", "Password": "pw" },
                    { "SlotName": "Bob", "Password": "" }
                  ]
                },
                {
                  "Name": "Shared",
                  "Host": "localhost",
                  "Port": 38282,
                  "Password": "room",
                  "Slots": [ { "SlotName": "Carol" } ]
                }
              ]
            }
            """);

        var imported = _service.Import(_exportFilePath);

        Assert.Equal(2, imported.Groups.Count);
        Assert.True(imported.Groups[0].Slots[0].RequiresPassword);
        Assert.False(imported.Groups[0].Slots[1].RequiresPassword);
        Assert.True(imported.Groups[1].Slots[0].RequiresPassword);
        Assert.All(imported.Groups, g => Assert.False(string.IsNullOrEmpty(g.Color)));
        Assert.NotEqual(imported.Groups[0].Color, imported.Groups[1].Color);
    }

    [Fact]
    public void Import_NewerFormatVersion_IsRejected()
    {
        _service.Export(_exportFilePath, new[] { NewGroup() }, settings: null);
        var root = JsonNode.Parse(File.ReadAllText(_exportFilePath))!;
        root["FormatVersion"] = ConfigExportService.CurrentFormatVersion + 1;
        File.WriteAllText(_exportFilePath, root.ToJsonString());

        var ex = Assert.Throws<ConfigImportException>(() => _service.Import(_exportFilePath));
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("""{ "Groups": [] }""")]
    [InlineData("""{ "FormatVersion": 0, "Groups": [] }""")]
    [InlineData("""{ "FormatVersion": "1", "Groups": [] }""")]
    [InlineData("""{ "FormatVersion": 1 }""")]
    [InlineData("""[ { "Name": "plain groups.json" } ]""")]
    [InlineData("not json at all")]
    public void Import_NotAnExport_IsRejected(string content)
    {
        File.WriteAllText(_exportFilePath, content);

        Assert.Throws<ConfigImportException>(() => _service.Import(_exportFilePath));
    }
}
