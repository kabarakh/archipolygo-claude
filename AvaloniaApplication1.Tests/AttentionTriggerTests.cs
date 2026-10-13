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
/// Item/chat trigger sites aren't covered: the fake's AllItemsReceived throws and ChatLogMessage/ItemSendLogMessage
/// only have internal constructors, so a fake session can't raise them.
/// </summary>
public class AttentionTriggerTests
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
    public void SyncHints_NewUnfoundLiveHint_ReportsOwnHint()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("new-hint", slot.Id) }, isLive: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { (groupViewModel.Group.Id, AttentionCategory.OwnHint) }, attentionTracker.Reports);
    }

    /// <summary>An already-found hint at first sight is historical noise (same condition as SyncHints' event-log entry).</summary>
    [AvaloniaFact]
    public void SyncHints_NewButAlreadyFoundHint_DoesNotReport()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("found-hint", slot.Id, found: true) }, isLive: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(attentionTracker.Reports);
    }

    [AvaloniaFact]
    public void SyncHints_ExistingHintJustFlippingFound_DoesNotReportAgain()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint", slot.Id) }, isLive: true);
        Dispatcher.UIThread.RunJobs();
        attentionTracker.Reports.Clear();

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("hint", slot.Id, found: true) }, isLive: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(attentionTracker.Reports);
    }

    /// <summary>Backlog (catch-up or initial replay) is flagged new but never asks for attention.</summary>
    [AvaloniaFact]
    public void SyncHints_NewUnfoundHintFromBacklog_IsAddedButNotReported()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.SyncHints(groupViewModel, new[] { MakeSnapshot("backlog-hint", slot.Id) }, isLive: false);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Assert.Single(groupViewModel.Hints).IsNewSinceLastSession);
        Assert.Empty(attentionTracker.Reports);
    }

    [AvaloniaFact]
    public void AddHintFromChat_NewUnfoundHint_ReportsOwnHint()
    {
        var groupViewModel = MakeHintTestGroup(out var slot);
        var attentionTracker = new FakeAttentionTracker();
        var hintService = new HintService(new InMemoryProfileSyncStateStore(), attentionTracker);

        hintService.AddHintFromChat(groupViewModel, MakeSnapshot("chat-hint", slot.Id));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { (groupViewModel.Group.Id, AttentionCategory.OwnHint) }, attentionTracker.Reports);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task IncomingDeathLink_ReportsDeathLink()
    {
        var factory = new FakeSessionFactory();
        var attentionTracker = new FakeAttentionTracker();
        var manager = new ConnectionManager(
            new NoOpMessageHistoryService(),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero,
            attentionTracker: attentionTracker);
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        factory.DeathLinkServicesBySession[session].RaiseDeathLinkReceived(new DeathLink("Bob", "Bob fell into lava."));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { (vm.Group.Id, AttentionCategory.DeathLink) }, attentionTracker.Reports);
    }
}
