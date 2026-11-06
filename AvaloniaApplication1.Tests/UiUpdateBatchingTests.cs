using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Archipolygo.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// A burst of thousands of lines must stream into the lists in time slices, with one list notification per slice,
/// instead of one full list rebuild per line. See Umsetzungsplan.md, "UI-Updates in Zeitscheiben".
/// </summary>
public class UiUpdateBatchingTests
{
    private static GroupViewModel MakeGroup()
    {
        var group = new ServerConnectionGroup { Name = "Test Server" };
        group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = "Alice" });
        return new GroupViewModel(group, new FakeConnectionManager());
    }

    private static EventEntry Line(string text, EventType type = EventType.Chat) => new() { Text = text, Type = type };

    private static List<NotifyCollectionChangedAction> RecordChanges(IEnumerable<object> collection)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, e) => actions.Add(e.Action);
        return actions;
    }

    // ── FilteredCollection ──

    [Fact]
    public void FilteredCollection_RangeAppend_TestsOnlyTheNewItems_InOneAdd()
    {
        var source = new RangeObservableCollection<string>(new[] { "a1", "b1" });
        var filtered = new FilteredCollection<string>(source, s => s.StartsWith('a'));
        var actions = RecordChanges(filtered);

        source.AddRange(new[] { "a2", "b2", "a3" });

        Assert.Equal(new[] { "a1", "a2", "a3" }, filtered);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, actions);
    }

    [Fact]
    public void FilteredCollection_TrimAtTheFront_RemovesOnlyThoseItems_WithoutAReset()
    {
        var source = new RangeObservableCollection<string>(new[] { "a1", "b1", "a2", "a3" });
        var filtered = new FilteredCollection<string>(source, s => s.StartsWith('a'));
        var actions = RecordChanges(filtered);

        source.RemoveRange(0, 3);

        Assert.Equal(new[] { "a3" }, filtered);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove }, actions);
    }

    [Fact]
    public void FilteredCollection_RemovalInTheMiddle_Rebuilds()
    {
        var source = new RangeObservableCollection<string>(new[] { "a1", "a2", "a3" });
        var filtered = new FilteredCollection<string>(source, s => s.StartsWith('a'));

        source.RemoveAll(s => s == "a2");

        Assert.Equal(new[] { "a1", "a3" }, filtered);
    }

    // ── GroupViewModel update batch ──

    [Fact]
    public void AppendEvent_InsideABatch_AppendsAndTrimsWithOneNotificationEach()
    {
        var group = MakeGroup();
        for (var i = 0; i < 5; i++)
        {
            group.AppendEvent(Line($"old {i}"), historyLimit: 6);
        }

        var actions = RecordChanges(group.VisibleEvents);

        group.BeginUpdateBatch();
        for (var i = 0; i < 4; i++)
        {
            group.AppendEvent(Line($"new {i}"), historyLimit: 6);
        }

        Assert.Empty(actions);
        group.EndUpdateBatch();

        Assert.Equal(new[] { "old 3", "old 4", "new 0", "new 1", "new 2", "new 3" }, group.VisibleEvents.Select(e => e.Text));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, actions);
    }

    [Fact]
    public void AppendEvent_BurstLargerThanTheLimit_KeepsOnlyTheNewest()
    {
        var group = MakeGroup();
        group.AppendEvent(Line("old"), historyLimit: 3);

        group.BeginUpdateBatch();
        for (var i = 0; i < 10; i++)
        {
            group.AppendEvent(Line($"new {i}"), historyLimit: 3);
        }

        group.EndUpdateBatch();

        Assert.Equal(new[] { "new 7", "new 8", "new 9" }, group.Events.Select(e => e.Text));
    }

    [Fact]
    public void HintCounters_InsideABatch_AreRaisedOnceAtTheEnd()
    {
        var group = MakeGroup();
        var raised = 0;
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GroupViewModel.UnfoundHintCount))
            {
                raised++;
            }
        };

        group.BeginUpdateBatch();
        for (var i = 0; i < 50; i++)
        {
            group.Hints.Add(new HintEntry { Key = $"h{i}", ItemName = $"Item {i}" });
        }

        group.Hints[0].Found = true;
        group.EndUpdateBatch();

        Assert.Equal(1, raised);
        Assert.Equal(49, group.UnfoundHintCount);
        Assert.Equal(49, group.VisibleHints.Count());
    }

    // ── UiUpdateQueue ──

    [AvaloniaFact]
    public void Queue_BurstOfFiveThousandLines_ReachesTheListInFewNotifications()
    {
        var group = MakeGroup();
        var queue = new UiUpdateQueue();
        var history = new MessageHistoryService(new FakePersistenceService(), new ProfileSyncStateStore(new FakePersistenceService()), queue);
        var actions = RecordChanges(group.VisibleEvents);

        for (var i = 0; i < 5000; i++)
        {
            history.HandleChatMessage(group, $"Line {i}", Array.Empty<EventTextSegment>(), slotId: null);
        }

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new AppSettings().EventHistoryLimit, group.Events.Count);
        Assert.Equal("Line 4999", group.Events[^1].Text);
        Assert.Equal($"Line {5000 - group.Events.Count}", group.Events[0].Text);
        Assert.True(actions.Count < 50, $"expected a few notifications per time slice, got {actions.Count}");
    }

    [AvaloniaFact]
    public void Queue_LetsInputPriorityWorkRunBetweenTimeSlices()
    {
        var group = MakeGroup();
        var queue = new UiUpdateQueue();
        int? appliedWhenInputRan = null;

        for (var i = 0; i < 200; i++)
        {
            var index = i;
            queue.Enqueue(group, () =>
            {
                if (index == 0)
                {
                    // Stands in for a click that arrives while the burst is being applied.
                    Dispatcher.UIThread.Post(() => appliedWhenInputRan = group.Events.Count, DispatcherPriority.Input);
                }

                Thread.Sleep(1);
                group.AppendEvent(Line($"Line {index}"));
            });
        }

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(200, group.Events.Count);
        Assert.NotNull(appliedWhenInputRan);
        Assert.InRange(appliedWhenInputRan!.Value, 1, 199);
    }

    // ── Dashboard ──

    [Fact]
    public void Dashboard_Append_KeepsExistingRowInstances_AndRaisesOnlyAnAdd()
    {
        var group = MakeGroup();
        group.AppendEvent(Line("first"));
        var dashboard = new DashboardViewModel(new ObservableCollection<GroupViewModel> { group }, _ => { }, (_, _, _) => { });
        var firstRow = dashboard.VisibleEvents.Single();
        var actions = RecordChanges(dashboard.VisibleEvents);

        group.AppendEvent(Line("second"));

        Assert.Equal(new[] { "first", "second" }, dashboard.VisibleEvents.Select(r => r.Event.Text));
        Assert.Same(firstRow, dashboard.VisibleEvents.First());
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, actions);
    }

    [Fact]
    public void Dashboard_TrimOfOneServer_RemovesOnlyItsRows()
    {
        var groupA = MakeGroup();
        var groupB = MakeGroup();
        var dashboard = new DashboardViewModel(new ObservableCollection<GroupViewModel> { groupA, groupB }, _ => { }, (_, _, _) => { });
        _ = dashboard.VisibleEvents.ToList();

        groupB.AppendEvent(Line("b old"), historyLimit: 1);
        groupA.AppendEvent(Line("a old"), historyLimit: 1);
        groupA.AppendEvent(Line("a new"), historyLimit: 1);
        groupB.AppendEvent(Line("b new"), historyLimit: 1);

        Assert.Equal(new[] { "a new", "b new" }, dashboard.VisibleEvents.Select(r => r.Event.Text));
    }

    [Fact]
    public void Dashboard_WhileHidden_DoesNoListWork_AndCatchesUpWhenShown()
    {
        var group = MakeGroup();
        var dashboard = new DashboardViewModel(new ObservableCollection<GroupViewModel> { group }, _ => { }, (_, _, _) => { });
        var visibleEvents = dashboard.VisibleEvents;
        var actions = RecordChanges(visibleEvents);

        dashboard.IsShown = false;
        group.AppendEvent(Line("while hidden"));

        Assert.Empty(actions);

        dashboard.IsShown = true;

        Assert.Equal(new[] { "while hidden" }, visibleEvents.Select(r => r.Event.Text));
    }

    // ── Real view ──

    [AvaloniaFact]
    public void Burst_IntoTheRealEventsList_WithASelectedRow_KeepsFollowingTheNewestLine()
    {
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), new FakeConnectionManager(), new MultiworldTrackerService());
        mainWindowViewModel.AddNewGroup("Server1", "host1", 1, string.Empty, "Alice", autoConnect: false);
        var group = mainWindowViewModel.Groups[0];
        var window = new MainWindow { DataContext = mainWindowViewModel, Width = 900, Height = 550 };
        window.Show();
        mainWindowViewModel.IsDashboardVisible = false;
        Dispatcher.UIThread.RunJobs();

        var queue = new UiUpdateQueue();
        var history = new MessageHistoryService(new FakePersistenceService(), new ProfileSyncStateStore(new FakePersistenceService()), queue);
        for (var i = 0; i < 100; i++)
        {
            history.HandleChatMessage(group, $"Warm-up {i}", Array.Empty<EventTextSegment>(), slotId: null);
        }

        Dispatcher.UIThread.RunJobs();

        var listBox = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "EventsListBox");
        listBox.SelectedIndex = 3;

        for (var i = 0; i < 3000; i++)
        {
            history.HandleChatMessage(group, $"Burst {i}", Array.Empty<EventTextSegment>(), slotId: null);
        }

        Dispatcher.UIThread.RunJobs();

        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().First();
        var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        Assert.Equal("Burst 2999", group.Events[^1].Text);
        Assert.True(distanceFromBottom < 1.0, $"expected the list to follow the newest line; distance from bottom={distanceFromBottom}");
    }
}
