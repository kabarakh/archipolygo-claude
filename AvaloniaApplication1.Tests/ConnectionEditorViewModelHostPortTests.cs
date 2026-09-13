using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="ConnectionEditorViewModel.TryResolveHostPortAsync"/> -
/// plain async C# logic (no session, no Avalonia layout) letting the
/// connection editor's host:port field also accept a room link/id, resolved
/// via a room_status lookup - see the doc comment on that method for the
/// full rationale.
/// </summary>
public class ConnectionEditorViewModelHostPortTests
{
    private static ConnectionEditorViewModel ForNewGroupWithResolver(System.Func<string, Task<RoomConnectionInfo?>>? resolveRoomConnectionInfo = null) =>
        ConnectionEditorViewModel.ForNewGroup(resolveRoomConnectionInfo: resolveRoomConnectionInfo);

    [Fact]
    public async Task PlainHostPort_SucceedsWithoutNeedingACallback()
    {
        var viewModel = ForNewGroupWithResolver();
        viewModel.HostPortInput = "archipelago.gg:38281";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
        Assert.Equal("archipelago.gg:38281", viewModel.HostPortInput); // untouched
    }

    [Fact]
    public async Task BareRoomId_ResolvesViaCallback_AndOverwritesHostPortInput()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: roomId =>
            Task.FromResult(roomId == "kK5fmxd8TfisU5Yp_eg"
                ? new RoomConnectionInfo { Host = "archipelago.gg", Port = 52122, TrackerId = "resolved-tracker-id" }
                : null));
        viewModel.HostPortInput = "kK5fmxd8TfisU5Yp_eg";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
        Assert.Equal("archipelago.gg:52122", viewModel.HostPortInput);
    }

    [Fact]
    public async Task RoomUrl_ResolvesViaCallback_AndFillsInEmptyTrackerField()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: roomId =>
            Task.FromResult(roomId == "kK5fmxd8TfisU5Yp_eg"
                ? new RoomConnectionInfo { Host = "archipelago.gg", Port = 52122, TrackerId = "resolved-tracker-id" }
                : null));
        viewModel.HostPortInput = "https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg";
        viewModel.SlotName = "Alice";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.True(ok);
        Assert.Equal("archipelago.gg:52122", viewModel.HostPortInput);
        Assert.Equal("resolved-tracker-id", viewModel.TrackerReferenceInput); // auto-filled since it was empty
    }

    [Fact]
    public async Task RoomUrl_DoesNotOverwriteAnAlreadyTypedTrackerField()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: _ =>
            Task.FromResult<RoomConnectionInfo?>(new RoomConnectionInfo { Host = "archipelago.gg", Port = 52122, TrackerId = "resolved-tracker-id" }));
        viewModel.HostPortInput = "https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg";
        viewModel.TrackerReferenceInput = "already-typed-tracker-id";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.True(ok);
        Assert.Equal("already-typed-tracker-id", viewModel.TrackerReferenceInput);
    }

    [Fact]
    public async Task RoomReference_CallbackFindsNothing_FailsWithValidationError()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: _ => Task.FromResult<RoomConnectionInfo?>(null));
        viewModel.HostPortInput = "https://archipelago.gg/room/does-not-exist";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.False(ok);
        Assert.NotNull(viewModel.ValidationError);
        Assert.Equal("https://archipelago.gg/room/does-not-exist", viewModel.HostPortInput); // left as-is on failure
    }

    [Fact]
    public async Task RoomReference_NoCallbackWiredUp_FailsWithValidationError()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: null);
        viewModel.HostPortInput = "https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.False(ok);
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public async Task ExplicitTrackerLink_IsRejectedInsteadOfBeingGuessedAsARoomId()
    {
        var viewModel = ForNewGroupWithResolver(resolveRoomConnectionInfo: _ =>
            Task.FromResult<RoomConnectionInfo?>(new RoomConnectionInfo { Host = "archipelago.gg", Port = 1 }));
        viewModel.HostPortInput = "https://archipelago.gg/tracker/2gVkMQgISGScA8wsvDZg5A";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.False(ok);
        Assert.Contains("tracker link", viewModel.ValidationError);
    }

    [Fact]
    public async Task UnrecognizedInput_FailsWithValidationErrorMentioningBothFormats()
    {
        var viewModel = ForNewGroupWithResolver();
        viewModel.HostPortInput = "not a host or a room link";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.False(ok);
        Assert.Contains("host:port", viewModel.ValidationError);
        Assert.Contains("room link", viewModel.ValidationError);
    }

    [Fact]
    public async Task AddSlotMode_NeverLooksAtTheField_AlwaysSucceeds()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        var viewModel = ConnectionEditorViewModel.ForAddSlot(group, new[] { new PlayerChoice { SlotName = "Bob", DisplayText = "Bob" } });
        viewModel.HostPortInput = "garbage that would otherwise fail";

        var ok = await viewModel.TryResolveHostPortAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
    }
}
