using System;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A/B (Test-Umsetzungsplan.md): the window-flash trigger
/// conditions from Feature-Plaene/Tab-Eigenes-Fenster.md's Phase 2 - a
/// genuinely new, still-unfound hint (<see cref="HintService"/>) and any
/// DeathLink (<see cref="Services.ConnectionManager"/>, Kategorie B via a
/// real session like <see cref="ConnectionManagerDeathLinkTests"/>). Both
/// verified against a <see cref="FakeWindowAttentionService"/> rather than a
/// real window - the interface itself never touches one (only the real
/// <c>WindowAttentionService</c> implementation does, which is what
/// resolves/flashes an actual <see cref="Avalonia.Controls.Window"/> and so
/// isn't covered by these headless tests).
///
/// The third trigger condition (a live, progression-flagged item receipt,
/// in <see cref="SessionEventTranslator"/>) isn't covered here - the shared
/// <see cref="FakeReceivedItemsHelper"/>'s <c>AllItemsReceived</c> throws
/// <see cref="System.NotImplementedException"/> (a pre-existing test-fake
/// gap, not introduced by this feature - no existing test exercises
/// <c>ConnectionManager</c>'s item-received path either), so exercising it
/// would mean building out that fake first. Honestly documented rather than
/// silently skipped, same spirit as <see cref="GroupReorderTests"/>' own
/// drag-gesture gap note.
/// </summary>
public class WindowAttentionTriggerTests
{
    private static GroupViewModel MakeHintTestGroup(out SlotProfile slot)
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static HintSnapshot MakeSnapshot(string key, Guid slotId, bool found = false) => new()
    {
        Key = key,
        SlotId = slotId,
        ReceivingPlayer = 1,
        FindingPlayer = 2,
        ReceivingPlayerName = "Bob",
        FindingPlayerName = "Alice",
        ItemName = "Sword",
        LocationName = "Chest",
        Found = found,
        ItemFlags = ItemFlags.None,
        ReceivingPlayerKind = EventTextSegmentKind.OtherSlotName,
        FindingPlayerKind = EventTextSegmentKind.OtherSlotName,
    };

    [AvaloniaFact]
    public void SyncHints_NewUnfoundHint_RequestsAttentionForThatGroup()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionService = new FakeWindowAttentionService();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionService);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("new-hint", slot.Id) });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { groupViewModel.Group.Id }, attentionService.RequestedGroupIds);
    }

    /// <summary>An already-found hint at first sight is historical noise - see SyncHints' own "only generate an event-log entry for unfound hints" comment; the flash trigger reuses that exact condition.</summary>
    [AvaloniaFact]
    public void SyncHints_NewButAlreadyFoundHint_DoesNotRequestAttention()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionService = new FakeWindowAttentionService();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionService);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("found-hint", slot.Id, found: true) });
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(attentionService.RequestedGroupIds);
    }

    /// <summary>A hint's Found flag flipping on a later re-sync is not a "new" arrival - no repeat flash for the same hint.</summary>
    [AvaloniaFact]
    public void SyncHints_ExistingHintJustFlippingFound_DoesNotRequestAttentionAgain()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionService = new FakeWindowAttentionService();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionService);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint", slot.Id) });
        Dispatcher.UIThread.RunJobs();
        attentionService.RequestedGroupIds.Clear();

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint", slot.Id, found: true) });
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(attentionService.RequestedGroupIds);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task IncomingDeathLink_RequestsAttentionForThatGroup()
    {
        var factory = new FakeSessionFactory();
        var attentionService = new FakeWindowAttentionService();
        var manager = new ConnectionManager(
            new NoOpMessageHistoryService(),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero,
            windowAttentionService: attentionService);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        factory.DeathLinkServicesBySession[session].RaiseDeathLinkReceived(new DeathLink("Bob", "Bob fell into lava."));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { vm.Group.Id }, attentionService.RequestedGroupIds);
    }
}
