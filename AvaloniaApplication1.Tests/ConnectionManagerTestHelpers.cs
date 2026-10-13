using System;
using Archipolygo.Models;
using Archipolygo.Services;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>A real ConnectionManager on a FakeSessionFactory with no-op message/hint services.</summary>
internal static class ConnectionManagerTestHelpers
{
    public static (ConnectionManager manager, FakeSessionFactory factory) MakeManager()
    {
        var factory = new FakeSessionFactory();

        // Zero delays: these plain Task.Delay calls would otherwise make every catch-up/retry test really wait seconds.
        var manager = new ConnectionManager(
            new NoOpMessageHistoryService(),
            new NoOpHintService(),
            factory,
            itemBacklogGracePeriod: TimeSpan.Zero,
            transientConnectRetryDelay: TimeSpan.Zero);

        return (manager, factory);
    }

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
