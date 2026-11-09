using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Packets;
using Archipelago.MultiClient.Net.Models;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// The watched slot through a real ConnectionManager: number, hint subscription and both catch-up paths.
/// Live chat lines aren't covered: ItemSendLogMessage has only internal constructors, so a fake can't raise one.
/// </summary>
public class WatchedSlotConnectionManagerTests
{
    private const string TrackerId = "tracker";
    private const int WatchedSlot = 12;

    private static PlayerInfo Player(int slot, string name, string game = "Super Metroid") =>
        new(0, slot, name, name, game, null, null);

    private static FakeArchipelagoSession LeaderSession()
    {
        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.Players.AllPlayers = new[] { Player(1, "Alice"), Player(3, "Mira"), Player(WatchedSlot, "malandor") };
        session.CompleteLoginSuccessfully(slot: 1);
        return session;
    }

    private sealed record Setup(
        ConnectionManager Manager, FakeSessionFactory Factory, FakeMultiworldTrackerService Tracker,
        GroupViewModel Group, InMemoryProfileSyncStateStore Store, List<SlotProfile> CompletedSyncs);

    private static Setup MakeSetup(string watchedSlotName = "malandor", string? trackerId = TrackerId, bool catchUpByLogin = false)
    {
        var factory = new FakeSessionFactory();
        var tracker = new FakeMultiworldTrackerService();
        var store = new InMemoryProfileSyncStateStore();
        var manager = new ConnectionManager(
            new MessageHistoryService(new FakePersistenceService(), store, new UiUpdateQueue()),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero,
            multiworldTrackerService: tracker);
        var (vm, group) = MakeGroup(manager, "Alice");
        group.WatchedSlotName = watchedSlotName;
        group.TrackerId = trackerId;
        group.WatchedCatchUpByLogin = catchUpByLogin;

        var completed = new List<SlotProfile>();
        manager.SlotInitialSyncCompleted += (_, slot) => completed.Add(slot);
        return new Setup(manager, factory, tracker, vm, store, completed);
    }

    private static async Task ConnectLeaderAsync(Setup setup, FakeArchipelagoSession leader)
    {
        setup.Factory.Enqueue(leader);
        await setup.Manager.SwitchLeaderAsync(setup.Group, setup.Group.Group.Slots[0]);
        await setup.Manager.WhenCatchUpQueueIdleAsync();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_ResolvesTheWatchedSlot_AndTracksItsHints()
    {
        var setup = MakeSetup();
        var leader = LeaderSession();

        await ConnectLeaderAsync(setup, leader);

        Assert.Equal(WatchedSlot, setup.Group.WatchedSlotNumber);
        Assert.True(setup.Group.GetRoomSlotStatus(WatchedSlot).IsWatched);
        Assert.Contains(WatchedSlot, leader.Hints.TrackedSlots);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task LeaderConnect_CatchesUpThroughTheTracker_WithNamesFromTheLeader()
    {
        var setup = MakeSetup();
        var leader = LeaderSession();
        leader.Items.ItemNames[1001] = "Varia Suit";
        leader.Locations.LocationNames[2001] = "Crystal Peak";
        setup.Tracker.SetReceivedItems(TrackerId, 0, WatchedSlot,
            [new TrackerReceivedItem(1001, 2001, 3, (int)ItemFlags.Advancement), new TrackerReceivedItem(1002, 2002, 3, 0)]);

        await ConnectLeaderAsync(setup, leader);

        Assert.Equal(new[] { (TrackerId, 0, WatchedSlot) }, setup.Tracker.GetReceivedItemsCalls);
        var row = Assert.Single(setup.Group.WatchedItems);
        Assert.Equal(("Varia Suit", "Mira", "Crystal Peak"), (row.ItemName, row.SenderName, row.LocationName));
        Assert.Equal(2, setup.Group.ReceivedItems.Count(i => i.SlotId == setup.Group.Group.WatchedSlotId));
        Assert.Equal(1, setup.Factory.CreatedSessions.Count); // no login as malandor
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task WithoutTracker_AndWithoutOptIn_NeverLogsInAsTheWatchedSlot()
    {
        var setup = MakeSetup(trackerId: null);

        await ConnectLeaderAsync(setup, LeaderSession());

        Assert.Single(setup.Factory.CreatedSessions);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task WithoutTracker_OptInLogin_CatchesUpOnce_OutsideTheStartupCounters()
    {
        var setup = MakeSetup(trackerId: null, catchUpByLogin: true);
        var watchedSession = new FakeArchipelagoSession(numericSlot: WatchedSlot);
        watchedSession.CompleteLoginSuccessfully(slot: WatchedSlot);
        setup.Factory.Enqueue(LeaderSession());
        setup.Factory.Enqueue(watchedSession);

        await setup.Manager.SwitchLeaderAsync(setup.Group, setup.Group.Group.Slots[0]);
        await setup.Manager.WhenCatchUpQueueIdleAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("malandor", watchedSession.LastLoginName);
        Assert.Equal(1, watchedSession.Socket.DisconnectCallCount);
        Assert.DoesNotContain(setup.CompletedSyncs, slot => slot.SlotName == "malandor");
        Assert.DoesNotContain(setup.Group.Group.Slots, slot => slot.SlotName == "malandor");
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task OptInLogin_WithAWrongPassword_NeverPrompts()
    {
        var setup = MakeSetup(trackerId: null, catchUpByLogin: true);
        var prompts = 0;
        setup.Manager.PasswordRequested = (_, _, _, _) =>
        {
            prompts++;
            return Task.FromResult(false);
        };
        var watchedSession = new FakeArchipelagoSession(numericSlot: WatchedSlot);
        watchedSession.LoginResultSource.SetResult(
            new LoginFailure(new ConnectionRefusedPacket { Errors = new[] { ConnectionRefusedError.InvalidPassword } }));
        setup.Factory.Enqueue(LeaderSession());
        setup.Factory.Enqueue(watchedSession);

        await setup.Manager.SwitchLeaderAsync(setup.Group, setup.Group.Group.Slots[0]);
        await setup.Manager.WhenCatchUpQueueIdleAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, prompts);
        Assert.Contains(setup.Group.Events, e => e.Type == EventType.Error && e.Text.Contains("malandor"));
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ConfiguredWatchedSlot_UsesTheSiblingCatchUp_NotTheTracker()
    {
        var setup = MakeSetup(watchedSlotName: "Alice");

        await ConnectLeaderAsync(setup, LeaderSession());

        Assert.Equal(1, setup.Group.WatchedSlotNumber);
        Assert.Empty(setup.Tracker.GetReceivedItemsCalls);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task RefreshWatchedSlot_WhileOnline_ResolvesAndCatchesUpTheNewSlot()
    {
        var setup = MakeSetup(watchedSlotName: "nobody-yet");
        var leader = LeaderSession();
        await ConnectLeaderAsync(setup, leader);
        Assert.Null(setup.Group.WatchedSlotNumber);

        setup.Group.Group.WatchedSlotName = "Mira";
        await setup.Manager.RefreshWatchedSlotAsync(setup.Group);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, setup.Group.WatchedSlotNumber);
        Assert.Contains(3, leader.Hints.TrackedSlots);
        Assert.Contains((TrackerId, 0, 3), setup.Tracker.GetReceivedItemsCalls);
    }
}
