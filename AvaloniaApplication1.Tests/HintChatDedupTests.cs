using System;
using System.Linq;
using Archipelago.MultiClient.Net.Enums;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="HintService.AddHintFromChat"/> -
/// hints announced by a "[Hint]: ..." chat line feed the Hints panel, but
/// never as a second entry for a hint that's already there (same key, or
/// same finder + location), in either arrival order relative to TrackHints'
/// <see cref="HintService.SyncHints"/>. [AvaloniaFact] for the same
/// Dispatcher-ownership reason as <see cref="HintServiceFilterTests"/>.
/// </summary>
public class HintChatDedupTests
{
    private static GroupViewModel MakeGroup(out SlotProfile slot)
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static HintSnapshot Snapshot(string key, Guid slotId, int findingPlayer = 2, string location = "Chest", bool found = false) => new()
    {
        Key = key,
        SlotId = slotId,
        ReceivingPlayer = 1,
        FindingPlayer = findingPlayer,
        ReceivingPlayerName = "Bob",
        FindingPlayerName = "Alice",
        ItemName = "Sword",
        LocationName = location,
        Found = found,
        ItemFlags = ItemFlags.None,
        ReceivingPlayerKind = EventTextSegmentKind.OwnSlotName,
        FindingPlayerKind = EventTextSegmentKind.OtherSlotName
    };

    [AvaloniaFact]
    public void AddHintFromChat_AddsHintEntry_WithoutAnEventOfItsOwn()
    {
        var group = MakeGroup(out var slot);
        var hintService = new HintService(new InMemoryProfileSyncStateStore());

        hintService.AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(group.Hints);
        // The chat line itself is the event - no second "Hint: ..." entry.
        Assert.Empty(group.Events);
    }

    [AvaloniaFact]
    public void AddHintFromChat_SameFinderAndLocation_IsNotAddedTwice()
    {
        var group = MakeGroup(out var slot);
        var hintService = new HintService(new InMemoryProfileSyncStateStore());

        hintService.AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id));
        // Replayed on the next connect - even with a differing key, finder +
        // location alone identifies the same hint.
        hintService.AddHintFromChat(group, Snapshot("other-key", slot.Id));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(group.Hints);
    }

    [AvaloniaFact]
    public void AddHintFromChat_DifferentLocation_IsASeparateHint()
    {
        var group = MakeGroup(out var slot);
        var hintService = new HintService(new InMemoryProfileSyncStateStore());

        hintService.AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id, location: "Chest"));
        hintService.AddHintFromChat(group, Snapshot("1:2:11:21", slot.Id, location: "Shop"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, group.Hints.Count);
    }

    [AvaloniaFact]
    public void TrackHintsFirst_ThenChat_KeepsOneEntry()
    {
        var group = MakeGroup(out var slot);
        var hintService = new HintService(new InMemoryProfileSyncStateStore());

        hintService.SyncHints(group, new[] { Snapshot("1:2:10:20", slot.Id) });
        hintService.AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(group.Hints);
        Assert.Single(group.Events.Where(e => e.Type == EventType.HintReceived));
    }

    [AvaloniaFact]
    public void ChatFirst_ThenTrackHints_KeepsOneEntry_AndStillUpdatesFound()
    {
        var group = MakeGroup(out var slot);
        var hintService = new HintService(new InMemoryProfileSyncStateStore());

        hintService.AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id));
        hintService.SyncHints(group, new[] { Snapshot("1:2:10:20", slot.Id, found: true) });
        Dispatcher.UIThread.RunJobs();

        var hint = Assert.Single(group.Hints);
        Assert.True(hint.Found);
    }

    [AvaloniaFact]
    public void AddHintFromChat_MarksHintAsSeen_SoItIsNotNewAfterRestart()
    {
        var group = MakeGroup(out var slot);
        var store = new InMemoryProfileSyncStateStore();
        new HintService(store).AddHintFromChat(group, Snapshot("1:2:10:20", slot.Id));
        Dispatcher.UIThread.RunJobs();

        var restartedGroup = MakeGroup(out _);
        restartedGroup.Group.Slots.Clear();
        restartedGroup.Group.Slots.Add(slot);
        new HintService(store).SyncHints(restartedGroup, new[] { Snapshot("1:2:10:20", slot.Id) });
        Dispatcher.UIThread.RunJobs();

        Assert.False(Assert.Single(restartedGroup.Hints).IsNewSinceLastSession);
    }
}
