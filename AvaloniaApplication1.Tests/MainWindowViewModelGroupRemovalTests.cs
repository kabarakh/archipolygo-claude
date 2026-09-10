using System;
using System.IO;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): removing a whole server also cleans
/// up its Feature-Plaene/Archiv/Hint-Eingabefeld.md DataPackage cache (an
/// explicit dev requirement - an orphaned cache folder for a server that no
/// longer exists is just clutter). Uses a real <see cref="PersistenceService"/>
/// against a temp directory (see <see cref="PersistenceServiceTests"/>'s
/// identical pattern) since <see cref="FakePersistenceService"/> silently
/// no-ops every save, which would make this untestable.
/// </summary>
public sealed class MainWindowViewModelGroupRemovalTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PersistenceService _persistenceService;

    public MainWindowViewModelGroupRemovalTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "Archipolygo-Tests-" + Guid.NewGuid());
        _persistenceService = new PersistenceService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RemoveSelectedGroupAsync_DeletesThatGroupsDataPackageCache_LeavesOtherGroupsAlone()
    {
        var mainWindowViewModel = new MainWindowViewModel(_persistenceService, new FakeConnectionManager(), new MultiworldTrackerService());

        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        mainWindowViewModel.AddNewGroup("Server2", "host2", 2, string.Empty, "Bob", autoConnect: false);
        var groupToRemove = mainWindowViewModel.Groups[0];
        var otherGroup = mainWindowViewModel.Groups[1];

        _persistenceService.SaveDataPackageCache(groupToRemove.Group.Id, "Kirby Super Star", new DataPackageCacheEntry { Checksum = "a" });
        _persistenceService.SaveDataPackageCache(otherGroup.Group.Id, "Kirby Super Star", new DataPackageCacheEntry { Checksum = "b" });

        mainWindowViewModel.SelectedGroup = groupToRemove;
        await mainWindowViewModel.RemoveSelectedGroupCommand.ExecuteAsync(null);

        Assert.Null(_persistenceService.LoadDataPackageCache(groupToRemove.Group.Id, "Kirby Super Star"));
        Assert.NotNull(_persistenceService.LoadDataPackageCache(otherGroup.Group.Id, "Kirby Super Star"));
    }
}
