using System;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Shared setup for Kategorie B (Test-Umsetzungsplan.md) - a real
/// <see cref="ConnectionManager"/> wired to a <see cref="FakeSessionFactory"/>
/// (so every "connect" is a <see cref="FakeArchipelagoSession"/> the test
/// fully controls) and no-op message/hint services (Kategorie B is about
/// locking/ordering, not event-log content).
/// </summary>
internal static class ConnectionManagerTestHelpers
{
    public static (ConnectionManager manager, FakeSessionFactory factory) MakeManager()
    {
        var factory = new FakeSessionFactory();

        // Zero, not the real ~2s defaults - these are plain Task.Delay calls
        // unrelated to a FakeArchipelagoSession's own (fully controllable)
        // login completion; without overriding them too, every test touching
        // a catch-up sync or a transient retry would still really wait
        // several seconds for nothing. See ConnectionManager's constructor
        // doc comment.
        var manager = new ConnectionManager(
            new NoOpMessageHistoryService(),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero);

        return (manager, factory);
    }

    /// <summary>Builds a group with one configured slot per name in <paramref name="slotNames"/>, in that order.</summary>
    public static (GroupViewModel viewModel, ServerConnectionGroup group) MakeGroup(IConnectionManager manager, params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Name = "Test Server", Host = "host", Port = 1 };
        var viewModel = new GroupViewModel(group, manager);

        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return (viewModel, group);
    }
}
