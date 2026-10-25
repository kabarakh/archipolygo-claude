using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

public class MainWindowViewModelAddSlotChoicesTests
{
    [Fact]
    public async Task GetAvailableSlotsToAdd_ShowsAliasWithoutTheRedundantSlotNameSuffix()
    {
        var connectionManager = new FakeConnectionManager
        {
            RoomPlayers = new[]
            {
                new PlayerInfo(0, 1, "Alice", "Alice", "Some Game", null, null),
                new PlayerInfo(0, 2, "CaptainHook", "KabaDone (CaptainHook)", "Some Game", null, null),
                new PlayerInfo(0, 3, "Bob", "Bobby", "Some Game", null, null),
            },
        };
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), connectionManager, new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);

        var choices = await mainWindowViewModel.GetAvailableSlotsToAddAsync(mainWindowViewModel.Groups[0]);

        Assert.Equal(new[] { "CaptainHook (KabaDone)", "Bob (Bobby)" }, choices.Select(c => c.DisplayText));
    }
}
