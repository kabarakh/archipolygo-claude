using System;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Helpers;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="AdminPanelViewModel"/>'s
/// login/re-login/command flow (Admin-Funktionen.md in the feature-plan
/// archive) against <see cref="FakeConnectionManager"/>'s fake admin server,
/// which answers synchronously - so <see cref="AdminPanelViewModel.ReplyWindow"/>
/// can be zero and nothing here waits on real time.
/// </summary>
public class AdminPanelViewModelTests
{
    private static (GroupViewModel group, FakeConnectionManager manager, AdminPanelViewModel admin) MakeConnectedGroup()
    {
        var manager = new FakeConnectionManager();
        var serverGroup = new ServerConnectionGroup { Name = "Test Server", IsAdmin = true };
        var slot = new SlotProfile { GroupId = serverGroup.Id, SlotName = "Alice" };
        serverGroup.Slots.Add(slot);
        var group = new GroupViewModel(serverGroup, manager);
        manager.SwitchLeaderAsync(group, slot).Wait();

        group.Admin.ReplyWindow = TimeSpan.Zero;
        group.Admin.LoginTimeout = TimeSpan.FromMilliseconds(50);
        return (group, manager, group.Admin);
    }

    [Fact]
    public async Task Login_CorrectPassword_LogsIn()
    {
        var (_, manager, admin) = MakeConnectedGroup();

        var result = await admin.LoginAsync("admin");

        Assert.Equal(AdminLoginResult.Success, result);
        Assert.True(admin.IsLoggedIn);
        Assert.Contains("!admin login admin", manager.SentMessages);
    }

    [Fact]
    public async Task Login_WrongPassword_StaysLoggedOut()
    {
        var (_, _, admin) = MakeConnectedGroup();

        Assert.Equal(AdminLoginResult.WrongPassword, await admin.LoginAsync("nope"));
        Assert.False(admin.IsLoggedIn);
    }

    [Fact]
    public async Task Login_ServerWithoutAdminPassword_ReportsRemoteAdminDisabled()
    {
        var (_, manager, admin) = MakeConnectedGroup();
        manager.AdminServerPassword = null;

        Assert.Equal(AdminLoginResult.RemoteAdminDisabled, await admin.LoginAsync("admin"));
    }

    [Fact]
    public async Task Login_WhileDisconnected_SendsNothing()
    {
        var (group, manager, admin) = MakeConnectedGroup();
        await manager.DisconnectGroupAsync(group);
        manager.SentMessages.Clear();

        Assert.Equal(AdminLoginResult.NotConnected, await admin.LoginAsync("admin"));
        Assert.Empty(manager.SentMessages);
    }

    [Fact]
    public async Task NewLeaderConnection_LogsInAgainAutomatically()
    {
        var (group, manager, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        manager.SentMessages.Clear();

        // A reconnect/account switch: new socket, logged out server-side.
        await manager.SwitchLeaderAsync(group, group.Group.Slots[0]);

        Assert.Equal(new[] { "!admin login admin" }, manager.SentMessages);
        Assert.True(admin.IsLoggedIn);
    }

    [Fact]
    public async Task Command_AfterAnotherClientTookOverAdmin_LogsInAgainAndResends()
    {
        var (group, manager, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        manager.SimulateOtherClientTakesOverAdmin(group);
        manager.SentMessages.Clear();
        var player = new AdminPlayerRowViewModel(() => DateTimeOffset.UtcNow) { Team = 0, Slot = 2, Name = "Mira", Game = "Super Metroid" };

        var answer = await admin.SendItemAsync(player, "Missile", 1);

        Assert.Equal(new[]
        {
            "!admin /send \"Mira\" \"Missile\"",
            "!admin login admin",
            "!admin /send \"Mira\" \"Missile\"",
        }, manager.SentMessages);
        Assert.Contains("Cheat console: sending", answer);
        Assert.True(admin.IsLoggedIn);
    }

    [Fact]
    public async Task SetOption_ReturnsTheServersDirectAnswer()
    {
        var (_, _, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");

        var answer = await admin.SetOptionAsync("hint_cost", "5");

        Assert.Equal("Set option hint_cost to 5", answer);
    }

    [Fact]
    public async Task ChangedServerPassword_IsUsedForTheNextAutomaticLogin()
    {
        var (group, manager, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        var settings = new AdminSettingsViewModel(admin, null) { NewAdminPassword = "new-secret" };

        await settings.SaveAsync();
        manager.SentMessages.Clear();
        await manager.SwitchLeaderAsync(group, group.Group.Slots[0]);

        Assert.Equal(new[] { "!admin login new-secret" }, manager.SentMessages);
        Assert.True(admin.IsLoggedIn);
    }

    /// <summary>The admin password must never end up anywhere readable after login: not in the event log (server answers are masked), not in the message history.</summary>
    [Fact]
    public async Task AdminPassword_NeverAppearsInTheEventLog()
    {
        var (group, _, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        var settings = new AdminSettingsViewModel(admin, null) { NewAdminPassword = "brand-new-secret" };

        await settings.SaveAsync();

        Assert.DoesNotContain(group.Events, e => e.Text.Contains("brand-new-secret") || e.EffectiveSegments.Any(s => s.Text.Contains("brand-new-secret")));
        Assert.DoesNotContain("brand-new-secret", settings.ResultText);
    }

    [Fact]
    public async Task Settings_SendsOnlyChangedValues()
    {
        var (_, manager, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        manager.SentMessages.Clear();
        var current = new RoomSettingsSnapshot
        {
            ReleaseMode = "auto", CollectMode = "auto", RemainingMode = "goal",
            HintCostPercentage = 10, LocationCheckPoints = 1, HasPassword = false,
        };
        var settings = new AdminSettingsViewModel(admin, current) { ReleaseMode = "goal", HintCost = 10 };

        await settings.SaveAsync();

        Assert.Equal(new[] { "!admin /option release_mode \"goal\"" }, manager.SentMessages);
    }

    [Fact]
    public async Task Logout_ForgetsThePassword_SoAReconnectDoesNotLogInAgain()
    {
        var (group, manager, admin) = MakeConnectedGroup();
        await admin.LoginAsync("admin");
        group.SelectedRightPanel = RightPanelView.Admin;

        await admin.LogoutCommand.ExecuteAsync(null);
        manager.SentMessages.Clear();
        await manager.SwitchLeaderAsync(group, group.Group.Slots[0]);

        Assert.False(admin.IsLoggedIn);
        Assert.Empty(manager.SentMessages);
        Assert.Equal(RightPanelView.Hints, group.SelectedRightPanel);
    }

    [Fact]
    public async Task RefreshPlayers_JoinsRosterWithTrackerDataByTeamAndSlot()
    {
        var manager = new FakeConnectionManager();
        var tracker = new FakeMultiworldTrackerService();
        var serverGroup = new ServerConnectionGroup { Name = "Test Server", TrackerId = "t1" };
        serverGroup.Slots.Add(new SlotProfile { GroupId = serverGroup.Id, SlotName = "Alice" });
        var group = new GroupViewModel(serverGroup, manager, tracker);
        manager.RoomPlayers = new[]
        {
            new PlayerInfo(0, 1, "Alice", "Alice", "Game A", null, null),
            new PlayerInfo(0, 2, "Bob", "Bobby", "Game B", null, null),
        };
        var lastActivity = DateTimeOffset.UtcNow - TimeSpan.FromDays(4);
        tracker.SetProgress("t1", new RoomProgressSnapshot
        {
            Players = new[]
            {
                new PlayerProgress { Team = 0, Player = 2, ChecksDone = 5, ChecksTotal = 10, ClientStatus = 30, LastActivity = lastActivity, CheckedLocationIds = new long[] { 7 } },
            },
        });

        await group.Admin.RefreshPlayersAsync();

        var bob = group.Admin.Players.Single(p => p.Name == "Bob");
        Assert.True(bob.HasTrackerData);
        Assert.Equal("Goal", bob.StatusText);
        Assert.Equal("5/10 checks", bob.ChecksText);
        Assert.True(bob.IsInactiveWarning);
        Assert.True(bob.IsLocationChecked(7));
        var alice = group.Admin.Players.Single(p => p.Name == "Alice");
        Assert.True(alice.IsOwnSlot);
        Assert.False(alice.HasTrackerData);
    }

    [Fact]
    public async Task ExcludeGoaled_HidesFinishedPlayers_AndShowsThemAgainWhenTurnedOff()
    {
        var manager = new FakeConnectionManager();
        var tracker = new FakeMultiworldTrackerService();
        var serverGroup = new ServerConnectionGroup { Name = "Test Server", TrackerId = "t1" };
        var group = new GroupViewModel(serverGroup, manager, tracker);
        manager.RoomPlayers = new[]
        {
            new PlayerInfo(0, 1, "Playing", "Playing", "G", null, null),
            new PlayerInfo(0, 2, "Finished", "Finished", "G", null, null),
        };
        tracker.SetProgress("t1", new RoomProgressSnapshot
        {
            Players = new[]
            {
                new PlayerProgress { Team = 0, Player = 1, ChecksDone = 1, ClientStatus = 20 },
                new PlayerProgress { Team = 0, Player = 2, ChecksDone = 9, ClientStatus = 30 },
            },
        });
        await group.Admin.RefreshPlayersAsync();

        group.Admin.ExcludeGoaled = true;
        Assert.Equal(new[] { "Playing" }, group.Admin.Players.Select(p => p.Name));

        group.Admin.ExcludeGoaled = false;
        Assert.Equal(2, group.Admin.Players.Count);
    }

    [Theory]
    [InlineData(2.9, false, false)]
    [InlineData(3.0, true, false)]
    [InlineData(4.9, true, false)]
    [InlineData(5.0, false, true)]
    public void InactivityHighlight_YellowFromThreeDays_RedFromFive(double days, bool warning, bool critical)
    {
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        var row = new AdminPlayerRowViewModel(() => now)
        {
            Team = 0, Slot = 1, Name = "P", Game = "G", HasTrackerData = true, LastActivity = now - TimeSpan.FromDays(days),
        };

        Assert.Equal(warning, row.IsInactiveWarning);
        Assert.Equal(critical, row.IsInactiveCritical);
    }

    /// <summary>Regression: PlayerInfo.Alias can already be "Done (LinkMK8Dx)" - must not show as "Done (LinkMK8Dx) (LinkMK8Dx)".</summary>
    [Theory]
    [InlineData("LinkMK8Dx", "Done (LinkMK8Dx)", "LinkMK8Dx (Done)")]
    [InlineData("LinkMK8Dx", "Done", "LinkMK8Dx (Done)")]
    [InlineData("LinkMK8Dx", "LinkMK8Dx", "LinkMK8Dx")]
    [InlineData("LinkMK8Dx", null, "LinkMK8Dx")]
    public void PlayerRowDisplayName_UsesTheSlotNameAliasRule(string name, string? alias, string expected)
    {
        var row = new AdminPlayerRowViewModel(() => DateTimeOffset.UtcNow) { Team = 0, Slot = 1, Name = name, Alias = alias, Game = "G" };

        Assert.Equal(expected, row.DisplayName);
    }

    [Fact]
    public void TurningIsAdminOff_LeavesTheAdminView()
    {
        var (group, _, _) = MakeConnectedGroup();
        group.SelectedRightPanel = RightPanelView.Admin;

        group.Group.IsAdmin = false;

        Assert.False(group.ShowAdminButton);
        Assert.Equal(RightPanelView.Hints, group.SelectedRightPanel);
    }

    [Fact]
    public void HandTypedAdminLogin_IsNotKeptInTheMessageHistory()
    {
        var (group, _, _) = MakeConnectedGroup();
        group.MessageToSend = "hello";
        group.SendMessageCommand.Execute(null);
        group.MessageToSend = "!admin login secret";
        group.SendMessageCommand.Execute(null);

        group.RecallPreviousMessage();

        Assert.Equal("hello", group.MessageToSend);
    }
}
