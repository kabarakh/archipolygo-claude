using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="ConnectionEditorViewModel.TryResolveTrackerReferenceAsync"/> -
/// plain async C# logic (no session, no Avalonia layout) driving Tier 2 of
/// Feature-Plaene/Archiv/Fortschrittsanzeigen.md. The UI-visibility/binding side of
/// this (the "Multiworld tracker" field itself) is covered separately in
/// <see cref="ConnectionEditorWindowProgressTests"/>.
/// </summary>
public class ConnectionEditorViewModelTrackerTests
{
    private static ConnectionEditorViewModel ForEditGroupWithResolver(System.Func<string, Task<string?>>? resolveTrackerId = null)
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        return ConnectionEditorViewModel.ForEditGroup(group, resolveTrackerId: resolveTrackerId);
    }

    [Fact]
    public async Task EmptyInput_SucceedsWithNoResolvedTracker()
    {
        var viewModel = ForEditGroupWithResolver();
        viewModel.TrackerReferenceInput = string.Empty;

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.Null(result.TrackerId);
    }

    [Fact]
    public async Task BareTrackerId_ResolvesWithoutNeedingACallback()
    {
        var viewModel = ForEditGroupWithResolver(resolveTrackerId: null);
        viewModel.TrackerReferenceInput = "2gVkMQgISGScA8wsvDZg5A";

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.True(ok);
        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.Equal("2gVkMQgISGScA8wsvDZg5A", result.TrackerId);
    }

    [Fact]
    public async Task RoomUrl_ResolvesViaCallback()
    {
        var viewModel = ForEditGroupWithResolver(resolveTrackerId: roomId =>
            Task.FromResult<string?>(roomId == "kK5fmxd8TfisU5Yp_eg" ? "resolved-tracker-id" : null));
        viewModel.TrackerReferenceInput = "https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg";

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
        Assert.True(viewModel.TryBuildResult(out var result));
        Assert.Equal("resolved-tracker-id", result.TrackerId);
        Assert.Equal("https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg", result.TrackerReferenceInput);
    }

    [Fact]
    public async Task RoomUrl_CallbackFindsNothing_FailsWithValidationError()
    {
        var viewModel = ForEditGroupWithResolver(resolveTrackerId: _ => Task.FromResult<string?>(null));
        viewModel.TrackerReferenceInput = "https://archipelago.gg/room/does-not-exist";

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.False(ok);
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public async Task RoomUrl_NoCallbackWiredUp_FailsWithValidationError()
    {
        var viewModel = ForEditGroupWithResolver(resolveTrackerId: null);
        viewModel.TrackerReferenceInput = "https://archipelago.gg/room/kK5fmxd8TfisU5Yp_eg";

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.False(ok);
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public async Task UnrecognizedInput_FailsWithValidationError()
    {
        var viewModel = ForEditGroupWithResolver();
        viewModel.TrackerReferenceInput = "https://archipelago.gg/sphere_tracker/xyz";

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.False(ok);
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public async Task AddSlotMode_NeverLooksAtTheField_AlwaysSucceeds()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        var viewModel = ConnectionEditorViewModel.ForAddSlot(group, new[] { new PlayerChoice { SlotName = "Bob", DisplayText = "Bob" } });

        var ok = await viewModel.TryResolveTrackerReferenceAsync();

        Assert.True(ok);
        Assert.Null(viewModel.ValidationError);
    }
}
