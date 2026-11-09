using System;
using System.Collections.Generic;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>The watched slot's catch-up history, config import and "new" bookkeeping.</summary>
public class WatchedSlotServiceTests
{
    private static GroupViewModel MakeGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server", WatchedSlotName = "malandor" };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static WatchedHistoryItem Item(string name, ItemFlags flags) =>
        new(name, $"{name} spot", "Mira", EventTextSegmentKind.OtherSlotName, flags);

    private static readonly IReadOnlyList<WatchedHistoryItem> History =
    [
        Item("Morph Ball", ItemFlags.Advancement),
        Item("Missile", ItemFlags.None),
        Item("Varia Suit", ItemFlags.Advancement),
        Item("Energy Tank", ItemFlags.NeverExclude),
        Item("Space Jump", ItemFlags.Advancement),
    ];

    private static (MessageHistoryService Service, InMemoryProfileSyncStateStore Store) MakeHistoryService()
    {
        var store = new InMemoryProfileSyncStateStore();
        return (new MessageHistoryService(new FakePersistenceService(), store, new UiUpdateQueue()), store);
    }

    [AvaloniaFact]
    public void ApplyWatchedSlotHistory_LogsOnlyUnseenItems_AndAdvancesTheIndex()
    {
        var group = MakeGroup();
        var (service, store) = MakeHistoryService();
        var watchedId = group.Group.WatchedSlotId;
        store.Save(new ProfileSyncState { ProfileId = watchedId, LastSeenItemIndex = 3 });

        service.ApplyWatchedSlotHistory(group, watchedId, "malandor", group.GetRoomSlotStatus(12), History);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "Energy Tank", "Space Jump" },
            group.Events.Select(e => e.Segments[2].Text));
        Assert.All(group.Events, e =>
        {
            Assert.Equal(watchedId, e.SlotId);
            Assert.True(e.IsNewSinceLastSession);
            Assert.Same(group.GetRoomSlotStatus(12), e.Receiver);
        });
        Assert.Equal(5, store.Get(watchedId).LastSeenItemIndex);
    }

    [AvaloniaFact]
    public void ApplyWatchedSlotHistory_ReplacesItemsAndStrip_MarkingOnlyUnseenRowsNew()
    {
        var group = MakeGroup();
        var (service, store) = MakeHistoryService();
        var watchedId = group.Group.WatchedSlotId;
        store.Save(new ProfileSyncState { ProfileId = watchedId, LastSeenItemIndex = 3 });
        group.ReceivedItems.Add(new ReceivedItemEntry
        {
            SlotId = watchedId, ReceivingSlotName = "malandor", ItemName = "stale", LocationName = "x", SenderName = "y",
            ItemKind = EventTextSegmentKind.ItemOther, SenderKind = EventTextSegmentKind.OtherSlotName,
        });

        service.ApplyWatchedSlotHistory(group, watchedId, "malandor", group.GetRoomSlotStatus(12), History);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(History.Select(i => i.ItemName), group.ReceivedItems.Select(i => i.ItemName));
        Assert.Equal(new[] { ("Space Jump", true), ("Varia Suit", false), ("Morph Ball", false) },
            group.WatchedItems.Select(row => (row.ItemName, row.IsNew)));
    }

    [AvaloniaFact]
    public void ApplyWatchedSlotHistory_ShorterThanTheIndex_TreatsItAsANewRoom()
    {
        var group = MakeGroup();
        var (service, store) = MakeHistoryService();
        var watchedId = group.Group.WatchedSlotId;
        store.Save(new ProfileSyncState { ProfileId = watchedId, LastSeenItemIndex = 40 });

        service.ApplyWatchedSlotHistory(group, watchedId, "malandor", group.GetRoomSlotStatus(12), History);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, group.Events.Count);
        Assert.Equal(5, store.Get(watchedId).LastSeenItemIndex);
    }

    [AvaloniaFact]
    public void NoteNewWatchedItems_OnScreen_MarksSeenAtOnce()
    {
        var windows = new FakeWindowAttentionService();
        var tracker = new AttentionTracker(windows, new FakePersistenceService());
        var group = MakeGroup();
        windows.MainWindow.IsActive = true;
        windows.MainWindowShowingGroupId = group.Group.Id;
        group.AddWatchedItem(new WatchedItemRow { ItemName = "Bomb", SenderName = "Mira", LocationName = "x", IsNew = true });

        tracker.NoteNewWatchedItems(group);

        Assert.False(group.HasNewWatchedItems);
    }

    [AvaloniaFact]
    public void NoteNewWatchedItems_OffScreen_StaysNewUntilTheGroupIsSeen()
    {
        var windows = new FakeWindowAttentionService();
        var tracker = new AttentionTracker(windows, new FakePersistenceService());
        var group = MakeGroup();
        group.AddWatchedItem(new WatchedItemRow { ItemName = "Bomb", SenderName = "Mira", LocationName = "x", IsNew = true });

        tracker.NoteNewWatchedItems(group);
        tracker.RefreshSeenState();
        Assert.True(group.HasNewWatchedItems);

        windows.MainWindow.IsActive = true;
        windows.MainWindowShowingGroupId = group.Group.Id;
        tracker.RefreshSeenState();

        Assert.False(group.HasNewWatchedItems);
    }

    [Fact]
    public void ConfigImport_CopiesWatchedSettings_AndKeepsTheLocalIdForTheSameSlot()
    {
        var local = new ServerConnectionGroup { Host = "host", Port = 1, WatchedSlotName = "malandor" };
        var imported = new ServerConnectionGroup
        {
            Host = "host", Port = 1, WatchedSlotName = "MALANDOR", WatchedItemCount = 5, WatchedCatchUpByLogin = true,
        };

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.Equal("MALANDOR", replacement.WatchedSlotName);
        Assert.Equal(5, replacement.WatchedItemCount);
        Assert.True(replacement.WatchedCatchUpByLogin);
        Assert.Equal(local.WatchedSlotId, replacement.WatchedSlotId);
    }

    [Fact]
    public void ConfigImport_DifferentWatchedSlot_GetsAFreshId()
    {
        var local = new ServerConnectionGroup { Host = "host", Port = 1, WatchedSlotName = "malandor" };
        var imported = new ServerConnectionGroup { Host = "host", Port = 1, WatchedSlotName = "Mira" };

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.NotEqual(local.WatchedSlotId, replacement.WatchedSlotId);
    }

    [Fact]
    public void WatchedProgressionItem_IsOnByDefault()
    {
        var setting = new AppSettings().GetAttentionCategorySetting(AttentionCategory.WatchedProgressionItem);

        Assert.True(setting.Count);
        Assert.True(setting.Blink);
    }
}
