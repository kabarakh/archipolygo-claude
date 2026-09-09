using System;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (Test-Umsetzungsplan.md): <see cref="MainWindowViewModel"/>'s
/// own <c>CatchUpNewSlotsSequentiallyAsync</c> (private, only reachable via
/// <see cref="MainWindowViewModel.AddSlotsToGroup"/>) - a different layer
/// than <see cref="Archipolygo.Services.ConnectionManager"/>'s own gate
/// (see <see cref="ConnectionManagerCatchUpTests"/>), but the same "one
/// slot's catch-up at a time" invariant: firing several new slots'
/// connects concurrently is exactly the simultaneous-handshake burst this
/// exists to avoid.
/// </summary>
public class MainWindowViewModelCatchUpTests
{
    [AvaloniaFact(Timeout = 5000)]
    public async Task AddSlotsToGroup_CatchesUpMultipleNewSlots_OneAtATime()
    {
        var factory = new FakeSessionFactory();
        var manager = new ConnectionManager(
            new NoOpMessageHistoryService(), new NoOpHintService(), factory,
            itemBacklogGracePeriod: TimeSpan.Zero, transientConnectRetryDelay: TimeSpan.Zero);
        var mainWindowViewModel = new MainWindowViewModel(new FakePersistenceService(), manager, new MultiworldTrackerService());

        var aliceSession = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(aliceSession);
        aliceSession.CompleteLoginSuccessfully(slot: 1);

        mainWindowViewModel.AddNewGroup("Test", "host", 1, string.Empty, "Alice", autoConnect: false);
        Dispatcher.UIThread.RunJobs();
        var groupViewModel = mainWindowViewModel.Groups[0];
        Assert.NotNull(groupViewModel.LeaderSlotId); // Alice is already the leader - AddSlotsToGroup below will trigger catch-up

        var bobSession = new FakeArchipelagoSession(numericSlot: 2);
        factory.Enqueue(bobSession);
        var carolSession = new FakeArchipelagoSession(numericSlot: 3);
        factory.Enqueue(carolSession);

        mainWindowViewModel.AddSlotsToGroup(groupViewModel, new[]
        {
            new StagedSlot { SlotName = "Bob", DisplayText = "Bob" },
            new StagedSlot { SlotName = "Carol", DisplayText = "Carol" },
        });
        Dispatcher.UIThread.RunJobs();

        // Bob's catch-up in flight; Carol's must not have started yet.
        Assert.Equal(2, factory.CreatedSessions.Count);
        Assert.Same(bobSession, factory.CreatedSessions[1]);

        bobSession.CompleteLoginSuccessfully(slot: 2);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, bobSession.Socket.DisconnectCallCount);
        Assert.Equal(3, factory.CreatedSessions.Count);
        Assert.Same(carolSession, factory.CreatedSessions[2]);
        Assert.Equal(0, carolSession.Socket.DisconnectCallCount);

        carolSession.CompleteLoginSuccessfully(slot: 3);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, carolSession.Socket.DisconnectCallCount);
    }
}
