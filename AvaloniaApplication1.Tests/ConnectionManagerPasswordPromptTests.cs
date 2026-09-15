using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using static AvaloniaApplication1.Tests.ConnectionManagerTestHelpers;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie B (see CLAUDE.md's test-category breakdown): Feature-Plaene/Passwort-Speicherung.md's
/// <c>SlotProfile.RequiresPassword</c> self-healing and the
/// <c>ConnectionManager.PasswordRequested</c> hook (proactive prompt,
/// InvalidPassword-triggered retry, quiet decline).
/// </summary>
public class ConnectionManagerPasswordPromptTests
{
    private static LoginFailure InvalidPasswordFailure() =>
        new(new ConnectionRefusedPacket { Errors = new[] { ConnectionRefusedError.InvalidPassword } });

    [AvaloniaFact(Timeout = 5000)]
    public async Task SuccessfulLogin_WithNonEmptyPassword_SetsRequiresPasswordTrue_AndPersists()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.Password = "secret"; // already known, e.g. typed into the editor - no prompt needed

        var persistRaisedFor = new List<GroupViewModel>();
        manager.GroupPersistNeeded += g => persistRaisedFor.Add(g);

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.True(alice.RequiresPassword);
        Assert.Contains(vm, persistRaisedFor);
        Assert.Equal("secret", session.LastPasswordArgument);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task SuccessfulLogin_WithEmptyPassword_SelfHealsRequiresPasswordBackToFalse()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.RequiresPassword = true; // stale belief from an earlier session

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        // No PasswordRequested handler set - RequiresPassword being true with
        // no known password just means the login is attempted empty, exactly
        // as it always was before this feature.
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.False(alice.RequiresPassword);
        Assert.Null(session.LastPasswordArgument);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task InvalidPasswordFailure_NoHandlerSet_SetsRequiresPasswordTrue_AndReportsErrorAsUsual()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.LoginResultSource.SetResult(InvalidPasswordFailure());

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        // Learned even with nobody around to act on it - see
        // ConnectionManager's own doc comment on this branch.
        Assert.True(alice.RequiresPassword);
        Assert.Single(factory.CreatedSessions); // never retried without a handler
        Assert.Null(vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Error, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ProactivePrompt_HandlerDeclines_FailsQuietly_NoErrorLogged_LeaderStateDisconnected()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.RequiresPassword = true; // known to need one, but nobody has it yet

        var promptCalls = new List<bool>();
        manager.PasswordRequested = (_, _, isRetry, _) =>
        {
            promptCalls.Add(isRetry);
            return Task.FromResult(false); // user declined
        };

        // A session object is still created (CreateSession runs before the
        // proactive check), but ConnectAsync()/LoginAsync() are never
        // reached - nothing needs enqueuing here, the factory just hands
        // back its own default session.
        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { false }, promptCalls); // isRetryAfterFailure: false
        var createdSession = Assert.Single(factory.CreatedSessions);
        Assert.Equal(0, createdSession.Socket.DisconnectCallCount); // never even attempted a close - nothing was ever opened
        Assert.Null(vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task ProactivePrompt_HandlerProvidesPassword_ConnectsWithIt()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.RequiresPassword = true;

        manager.PasswordRequested = (_, slot, _, _) =>
        {
            slot.Password = "fresh-password";
            return Task.FromResult(true);
        };

        var session = new FakeArchipelagoSession(numericSlot: 1);
        factory.Enqueue(session);
        session.CompleteLoginSuccessfully(slot: 1);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("fresh-password", session.LastPasswordArgument);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task InvalidPasswordFailure_HandlerRetriesWithFreshPassword_SucceedsOnSecondAttempt()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.Password = "wrong-password"; // already "known" - no proactive prompt

        var promptCalls = new List<bool>();
        manager.PasswordRequested = (_, slot, isRetry, _) =>
        {
            promptCalls.Add(isRetry);
            slot.Password = "correct-password";
            return Task.FromResult(true);
        };

        var firstAttempt = new FakeArchipelagoSession(numericSlot: 1);
        firstAttempt.LoginResultSource.SetResult(InvalidPasswordFailure());
        var secondAttempt = new FakeArchipelagoSession(numericSlot: 1);
        secondAttempt.CompleteLoginSuccessfully(slot: 1);
        factory.Enqueue(firstAttempt);
        factory.Enqueue(secondAttempt);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { true }, promptCalls); // isRetryAfterFailure: true
        Assert.Equal(2, factory.CreatedSessions.Count); // fresh session for the retry - not a MaxTransientConnectRetries slot
        Assert.Equal("wrong-password", firstAttempt.LastPasswordArgument);
        Assert.Equal("correct-password", secondAttempt.LastPasswordArgument);
        Assert.True(alice.RequiresPassword);
        Assert.Equal(alice.Id, vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
    }

    [AvaloniaFact(Timeout = 5000)]
    public async Task InvalidPasswordFailure_HandlerDeclinesRetry_FailsQuietly_NoErrorLogged()
    {
        var (manager, factory) = MakeManager();
        var (vm, group) = MakeGroup(manager, "Alice");
        var alice = group.Slots[0];
        alice.Password = "wrong-password";

        manager.PasswordRequested = (_, _, _, _) => Task.FromResult(false);

        var session = new FakeArchipelagoSession(numericSlot: 1);
        session.LoginResultSource.SetResult(InvalidPasswordFailure());
        factory.Enqueue(session);

        await manager.SwitchLeaderAsync(vm, alice);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(factory.CreatedSessions); // no retry attempted
        Assert.Null(vm.LeaderSlotId);
        Assert.Equal(ConnectionState.Disconnected, vm.ConnectionState); // not Error - a decline is not a real failure
    }
}
