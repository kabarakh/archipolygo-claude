using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;

namespace AvaloniaApplication1.Tests;

/// <summary>Uses a real PersistenceService on a temp directory, since the fake no-ops every save.</summary>
public sealed class MainWindowViewModelConfigImportTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
    private readonly PersistenceService _persistenceService;
    private readonly FakeConnectionManager _connectionManager = new();
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelConfigImportTests()
    {
        _persistenceService = new PersistenceService(_tempDirectory);
        _viewModel = new MainWindowViewModel(_persistenceService, _connectionManager, new MultiworldTrackerService());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static ServerConnectionGroup ImportedGroup(string host, int port, bool autoConnect, params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Name = $"Imported {host}", Host = host, Port = port, AutoConnect = autoConnect };
        foreach (var slotName in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = slotName });
        }

        return group;
    }

    private bool SyncStateFileExists(Guid slotId) => File.Exists(Path.Combine(_tempDirectory, "sync-state", $"{slotId}.json"));

    [Fact]
    public async Task NewGroups_AreAddedAndPersisted_OnlyAutoConnectOnesConnect()
    {
        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(
            new[] { ImportedGroup("offline.example", 1, autoConnect: false, "Alice") }, null));
        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(
            new[] { ImportedGroup("online.example", 2, autoConnect: true, "Bob") }, null));

        var offline = _viewModel.Groups.Single(g => g.Group.Host == "offline.example");
        var online = _viewModel.Groups.Single(g => g.Group.Host == "online.example");
        Assert.Null(offline.LeaderSlotId);
        Assert.Equal(online.Group.Slots[0].Id, online.LeaderSlotId);
        Assert.Contains(offline, _viewModel.DockedGroups);
        Assert.Equal(2, _persistenceService.LoadGroups().Count);
        Assert.False(string.IsNullOrEmpty(offline.Group.Color));
    }

    [Fact]
    public async Task OverwritingAConnectedGroup_UpdatesItInPlace_AndReconnects()
    {
        _viewModel.AddNewGroup("Local", "room.example", 38281, string.Empty, "Alice", autoConnect: true);
        var groupViewModel = _viewModel.Groups.Single();
        _viewModel.AddSlotsToGroup(groupViewModel, new[] { new StagedSlot { SlotName = "Dropped", DisplayText = "Dropped" } });
        var alice = groupViewModel.Group.Slots.Single(s => s.SlotName == "Alice");
        var dropped = groupViewModel.Group.Slots.Single(s => s.SlotName == "Dropped");
        _persistenceService.SaveSyncState(new ProfileSyncState { ProfileId = alice.Id });
        _persistenceService.SaveSyncState(new ProfileSyncState { ProfileId = dropped.Id });
        Assert.NotNull(groupViewModel.LeaderSlotId);

        // Imported AutoConnect off: the group still reconnects because it was online before.
        var imported = ImportedGroup("ROOM.example", 38281, autoConnect: false, "alice", "Carol");
        imported.Slots[0].Alias = "Ally";
        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(new[] { imported }, null));

        Assert.Same(groupViewModel, _viewModel.Groups.Single());
        Assert.Equal("Imported ROOM.example", groupViewModel.Group.Name);
        Assert.Equal(new[] { "alice", "Carol" }, groupViewModel.Group.Slots.Select(s => s.SlotName).OrderBy(n => n));
        Assert.Same(alice, groupViewModel.Group.Slots.Single(s => s.SlotName == "alice"));
        Assert.Equal("Ally", alice.Alias);
        Assert.True(SyncStateFileExists(alice.Id));
        Assert.False(SyncStateFileExists(dropped.Id));
        Assert.NotNull(groupViewModel.LeaderSlotId);
    }

    [Fact]
    public async Task OverwritingAnOfflineGroup_WithAutoConnectOff_StaysOffline()
    {
        _viewModel.AddNewGroup("Local", "room.example", 38281, string.Empty, "Alice", autoConnect: false);
        var groupViewModel = _viewModel.Groups.Single();
        await _connectionManager.DisconnectGroupAsync(groupViewModel);

        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(
            new[] { ImportedGroup("room.example", 38281, autoConnect: false, "Alice") }, null));

        Assert.Null(groupViewModel.LeaderSlotId);
        Assert.False(groupViewModel.Group.AutoConnect);
    }

    // AvaloniaFact: applying the theme touches Application.Current, which a parallel headless test may own.
    [AvaloniaFact]
    public async Task ImportedSettings_ReplaceTheLocalSettings_OnlyWhenIncluded()
    {
        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(
            Array.Empty<ServerConnectionGroup>(), null));
        Assert.Equal("System", _persistenceService.LoadSettings().ThemePreference);

        var settings = new AppSettings { ThemePreference = "Dark", EventsFiltersExpanded = true };
        await _viewModel.ApplyConfigImportAsync(new ConfigImportSelection(Array.Empty<ServerConnectionGroup>(), settings));

        Assert.Equal("Dark", _persistenceService.LoadSettings().ThemePreference);
        Assert.Equal("Dark", _viewModel.ThemePreference);
        Assert.True(_viewModel.FilterLayout.EventsFiltersExpanded);
    }
}
