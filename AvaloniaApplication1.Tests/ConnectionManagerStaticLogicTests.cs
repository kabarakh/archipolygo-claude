using System;
using System.Linq;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): only the pure, session-less helper
/// functions on <see cref="ConnectionManager"/> - no <c>ArchipelagoSession</c>,
/// no dispatcher, no Avalonia needed. These are <c>internal static</c> rather
/// than <c>private static</c> specifically so this project can call them
/// directly (see the <c>InternalsVisibleTo</c> in AvaloniaApplication1.csproj).
/// </summary>
public class ConnectionManagerStaticLogicTests
{
    [Fact]
    public void FilterToRealPlayers_RemovesServerSlotZero()
    {
        var players = new[]
        {
            MakePlayer(slot: 0, name: "Server"),
            MakePlayer(slot: 1, name: "Alice"),
        };

        var result = ConnectionManager.FilterToRealPlayers(players);

        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public void FilterToRealPlayers_RemovesItemLinkGroups()
    {
        var players = new[]
        {
            MakePlayer(slot: 1, name: "Alice"),
            MakePlayer(slot: 2, name: "ItemLinkGroup", groupMembers: new[] { 1 }),
        };

        var result = ConnectionManager.FilterToRealPlayers(players);

        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
    }

    [Fact]
    public void FilterToRealPlayers_KeepsEveryOtherRealPlayer()
    {
        var players = new[]
        {
            MakePlayer(slot: 0, name: "Server"),
            MakePlayer(slot: 1, name: "Alice"),
            MakePlayer(slot: 2, name: "Bob"),
            MakePlayer(slot: 3, name: "GroupEntry", groupMembers: new[] { 1, 2 }),
        };

        var result = ConnectionManager.FilterToRealPlayers(players);

        Assert.Equal(new[] { "Alice", "Bob" }, result.Select(p => p.Name));
    }

    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void IsTransientConnectFailure_ClassifiesTransientExceptionsAsTransient(Exception ex)
    {
        Assert.True(ConnectionManager.IsTransientConnectFailure(ex));
    }

    public static TheoryData<Exception> TransientExceptions() => new()
    {
        new TaskCanceledException(),
        new OperationCanceledException(),
        new TimeoutException(),
    };

    [Theory]
    [MemberData(nameof(NonTransientExceptions))]
    public void IsTransientConnectFailure_ClassifiesEverythingElseAsNotTransient(Exception ex)
    {
        Assert.False(ConnectionManager.IsTransientConnectFailure(ex));
    }

    public static TheoryData<Exception> NonTransientExceptions() => new()
    {
        new InvalidOperationException("bad password"),
        new Exception("generic failure"),
        new ArgumentException(),
    };

    private static PlayerInfo MakePlayer(int slot, string name, int[]? groupMembers = null) =>
        new(team: 0, slot: slot, name: name, alias: name, game: "Some Game", groups: null!, groupMembers: groupMembers!);
}
