using System;
using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="GroupViewModel.VisibleEvents"/>'s
/// <see cref="EventRelevanceFilter.FoundByMe"/> branch (Feature-Plaene/Log-Export.md) -
/// pure LINQ filtering over a plain <see cref="EventEntry"/> list, no
/// Dispatcher/Avalonia involvement, so plain <c>[Fact]</c> is correct here
/// (see <see cref="HintServiceFilterTests"/>'s own doc comment for when that
/// stops being true).
/// </summary>
public class GroupViewModelEventRelevanceTests
{
    private static GroupViewModel MakeGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        var slot = new SlotProfile { GroupId = group.Id, SlotName = "Alice" };
        group.Slots.Add(slot);
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static EventEntry MakeItemSendLine(string text, EventTextSegmentKind finderKind) => new()
    {
        Text = text,
        Type = EventType.ItemReceived,
        Segments = new[]
        {
            new EventTextSegment("Finder", finderKind),
            new EventTextSegment(" sent ", EventTextSegmentKind.PlainText),
            new EventTextSegment("Sword", EventTextSegmentKind.ItemOther),
            new EventTextSegment(" to Receiver (Chest)", EventTextSegmentKind.PlainText),
        },
    };

    private static EventEntry MakeDirectlyReceivedLine(string text) => new()
    {
        Text = text,
        Type = EventType.ItemReceived,
        Segments = new[]
        {
            new EventTextSegment("Received ", EventTextSegmentKind.PlainText),
            new EventTextSegment("Sword", EventTextSegmentKind.ItemOther),
            new EventTextSegment(" (Chest)", EventTextSegmentKind.PlainText),
        },
    };

    [Fact]
    public void FoundByMe_IncludesLineWhereThisGroupsLeaderIsTheFinder()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(MakeItemSendLine("own-find", EventTextSegmentKind.OwnSlotName));
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

        Assert.Equal(new[] { "own-find" }, groupViewModel.VisibleEvents.Select(e => e.Text));
    }

    [Fact]
    public void FoundByMe_IncludesLineWhereAConnectedSiblingSlotIsTheFinder()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(MakeItemSendLine("sibling-find", EventTextSegmentKind.ConnectedSlotName));
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

        Assert.Equal(new[] { "sibling-find" }, groupViewModel.VisibleEvents.Select(e => e.Text));
    }

    [Fact]
    public void FoundByMe_ExcludesLineFoundByAStranger()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(MakeItemSendLine("stranger-find", EventTextSegmentKind.OtherSlotName));
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

        Assert.Empty(groupViewModel.VisibleEvents);
    }

    [Fact]
    public void FoundByMe_ExcludesThisSlotsOwnDirectlyReceivedItem()
    {
        // BuildItemReceivedSegments' "Received " line - the finder here is
        // irrelevant to the *receiving* slot's own log, so its first segment
        // is never a classified player name and must not count as a find.
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(MakeDirectlyReceivedLine("direct-receive"));
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

        Assert.Empty(groupViewModel.VisibleEvents);
    }

    [Fact]
    public void FoundByMe_ExcludesNonItemEvents_EvenWithAnOwnSlotNameFirstSegment()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(new EventEntry
        {
            Text = "chat-line",
            Type = EventType.Chat,
            Segments = new[] { new EventTextSegment("Alice", EventTextSegmentKind.OwnSlotName) },
        });
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;

        Assert.Empty(groupViewModel.VisibleEvents);
    }

    [Fact]
    public void FoundByMe_StillHonorsTheItemCategoryCheckboxes()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(new EventEntry
        {
            Text = "trap-find",
            Type = EventType.ItemReceived,
            Segments = new[]
            {
                new EventTextSegment("Finder", EventTextSegmentKind.OwnSlotName),
                new EventTextSegment(" sent ", EventTextSegmentKind.PlainText),
                new EventTextSegment("Bomb", EventTextSegmentKind.ItemTrap),
                new EventTextSegment(" to Receiver (Chest)", EventTextSegmentKind.PlainText),
            },
        });
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.FoundByMe;
        groupViewModel.ShowTrapItemEvents = false;

        Assert.Empty(groupViewModel.VisibleEvents);
    }

    [Fact]
    public void All_DoesNotApplyTheFoundByMeRestriction()
    {
        var groupViewModel = MakeGroup();
        groupViewModel.Events.Add(MakeItemSendLine("stranger-find", EventTextSegmentKind.OtherSlotName));
        groupViewModel.SelectedEventRelevanceFilter = EventRelevanceFilter.All;

        Assert.Equal(new[] { "stranger-find" }, groupViewModel.VisibleEvents.Select(e => e.Text));
    }
}
