using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

public enum AdminLoginResult
{
    Success,
    WrongPassword,
    RemoteAdminDisabled,

    /// <summary>The server didn't answer "!admin login" in time.</summary>
    NoAnswer,

    NotConnected,
}

/// <summary>
/// Everything behind one server tab's Admin view (Admin-Funktionen.md in the
/// feature-plan archive): the "!admin login" state, sending admin commands
/// and collecting the server's answers, and the room's player list.
///
/// The server never correlates a reply with the command that caused it, and
/// most successful admin actions ("/send", "/release", ...) don't reply at
/// all - they're only announced room-wide. So a command's "answer" here is
/// whatever command result/broadcast text arrives on the leader session
/// within a short window after sending (see <see cref="SendCommandAsync"/>);
/// commands are sent one at a time so those windows never overlap.
///
/// The login belongs to the leader's socket: a new leader connection (account
/// switch via "Chat as", reconnect after a drop) is logged out server-side,
/// and so is this client when any other client logs in as admin (the server
/// has exactly one admin and doesn't tell the previous one). Both are healed
/// automatically while the password is still in memory - never persisted,
/// same as <see cref="ServerConnectionGroup.Password"/>.
/// </summary>
public partial class AdminPanelViewModel : ViewModelBase
{
    private static readonly TimeSpan DefaultLoginTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultReplyWindow = TimeSpan.FromMilliseconds(1500);

    private readonly GroupViewModel _group;
    private readonly IConnectionManager _connectionManager;
    private readonly IMultiworldTrackerService? _trackerService;
    private readonly Func<DateTimeOffset> _clock;
    private readonly DispatcherTimer? _ticker;

    // Commands (and logins) are strictly one at a time - see class comment.
    private readonly System.Threading.SemaphoreSlim _sendGate = new(1, 1);

    private string? _password;
    private List<(ServerReplyKind Kind, string Text)>? _collectedReplies;
    private TaskCompletionSource<AdminLoginResult>? _pendingLogin;
    private TaskCompletionSource<bool>? _pendingDirectReply;

    public AdminPanelViewModel(GroupViewModel group, IConnectionManager connectionManager, IMultiworldTrackerService? trackerService,
        Func<DateTimeOffset>? clock = null)
    {
        _group = group;
        _connectionManager = connectionManager;
        _trackerService = trackerService;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        _connectionManager.ServerReplyReceived += OnServerReplyReceived;
        _connectionManager.LeaderConnected += OnLeaderConnected;
        _group.PropertyChanged += OnGroupPropertyChanged;

        // Live-counting inactivity. No timer outside a running Avalonia app
        // (plain unit tests) - nothing would render it there anyway.
        if (Avalonia.Application.Current is not null)
        {
            _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _ticker.Tick += (_, _) => OnTick();
        }
    }

    /// <summary>Test seam: how long <see cref="LoginAsync"/> waits for the server's answer.</summary>
    internal TimeSpan LoginTimeout { get; set; } = DefaultLoginTimeout;

    /// <summary>Test seam: how long <see cref="SendCommandAsync"/> collects replies for a command without a direct answer.</summary>
    internal TimeSpan ReplyWindow { get; set; } = DefaultReplyWindow;

    public string ServerName => _group.Group.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginStatusText))]
    private bool _isLoggedIn;

    public string LoginStatusText => IsLoggedIn
        ? $"🛡 Admin via {_group.SelectedChatSlot?.DisplayName ?? "?"}"
        : "Not logged in as admin";

    // Every room player from the last refresh; Players is the filtered and
    // sorted view of it the list shows.
    private readonly List<AdminPlayerRowViewModel> _allPlayers = new();

    public ObservableCollection<AdminPlayerRowViewModel> Players { get; } = new();

    /// <summary>"Exclude goaled slots" - hides players the tracker reports as finished (<see cref="AdminPlayerRowViewModel.IsGoal"/>).</summary>
    [ObservableProperty]
    private bool _excludeGoaled;

    partial void OnExcludeGoaledChanged(bool value) => RebuildVisiblePlayers();

    public bool HasTracker => _group.HasMultiworldTracker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByName))]
    private bool _sortByInactivity = true;

    public bool SortByName => !SortByInactivity;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string? _refreshError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackerAgeText))]
    private DateTimeOffset? _trackerFetchedAt;

    public string TrackerAgeText
    {
        get
        {
            if (TrackerFetchedAt is not { } fetchedAt)
            {
                return string.Empty;
            }

            var age = _clock() - fetchedAt;
            return age.TotalMinutes >= 1
                ? $"Tracker data: {(int)age.TotalMinutes} min ago"
                : $"Tracker data: {Math.Max(0, (int)age.TotalSeconds)} s ago";
        }
    }

    // ── Login ──

    public async Task<AdminLoginResult> LoginAsync(string password)
    {
        await _sendGate.WaitAsync();
        try
        {
            return await LoginCoreAsync(password);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<AdminLoginResult> LoginCoreAsync(string password)
    {
        if (!_group.IsLeaderConnected)
        {
            return AdminLoginResult.NotConnected;
        }

        var pending = new TaskCompletionSource<AdminLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingLogin = pending;
        try
        {
            await _connectionManager.SendMessageAsync(_group, AdminCommands.Login(password));
            var completed = await Task.WhenAny(pending.Task, Task.Delay(LoginTimeout));
            var result = completed == pending.Task ? pending.Task.Result : AdminLoginResult.NoAnswer;

            if (result == AdminLoginResult.Success)
            {
                _password = password;
                IsLoggedIn = true;
                _ticker?.Start();
                _ = RefreshPlayersAsync();
            }

            return result;
        }
        finally
        {
            _pendingLogin = null;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _sendGate.WaitAsync();
        try
        {
            if (_group.IsLeaderConnected)
            {
                await _connectionManager.SendMessageAsync(_group, AdminCommands.Logout());
            }
        }
        finally
        {
            _sendGate.Release();
        }

        _password = null;
        IsLoggedIn = false;
        _ticker?.Stop();
        if (_group.SelectedRightPanel == RightPanelView.Admin)
        {
            _group.SelectedRightPanel = RightPanelView.Hints;
        }
    }

    /// <summary>After "/option server_password" succeeds - otherwise the next automatic re-login would use the old one.</summary>
    public void UpdateStoredPassword(string newPassword)
    {
        if (_password is not null)
        {
            _password = newPassword;
        }
    }

    private void OnLeaderConnected(GroupViewModel group)
    {
        if (group != _group || _password is null)
        {
            return;
        }

        // New socket = logged out server-side. Heal it quietly.
        IsLoggedIn = false;
        _ = ReloginAfterNewConnectionAsync(_password);
    }

    private async Task ReloginAfterNewConnectionAsync(string password)
    {
        var result = await LoginAsync(password);
        if (result != AdminLoginResult.Success)
        {
            _password = null;
            AddInfo($"Automatic admin login after reconnecting failed ({DescribeLoginFailure(result)}). Open the Admin view to log in again.");
            if (_group.SelectedRightPanel == RightPanelView.Admin)
            {
                _group.SelectedRightPanel = RightPanelView.Hints;
            }
        }
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GroupViewModel.IsLeaderConnected) when !_group.IsLeaderConnected:
                // Password stays in memory - OnLeaderConnected logs back in.
                IsLoggedIn = false;
                break;
            case nameof(GroupViewModel.SelectedChatSlot):
                OnPropertyChanged(nameof(LoginStatusText));
                break;
            case nameof(GroupViewModel.HasMultiworldTracker):
                OnPropertyChanged(nameof(HasTracker));
                break;
        }
    }

    public static string DescribeLoginFailure(AdminLoginResult result) => result switch
    {
        AdminLoginResult.WrongPassword => "Password incorrect.",
        AdminLoginResult.RemoteAdminDisabled => "Remote administration is disabled on this server - it has no admin password.",
        AdminLoginResult.NoAnswer => "The server didn't answer. Try again.",
        AdminLoginResult.NotConnected => "Not connected to the server.",
        _ => string.Empty,
    };

    // ── Commands ──

    /// <summary>
    /// Sends one "!admin ..." line and returns what the dialog shows as the
    /// server's answer. <paramref name="expectsDirectReply"/>: the command
    /// always answers with its own result line on success too ("/option"),
    /// so this can return as soon as that arrives instead of waiting out
    /// <see cref="ReplyWindow"/>. If the server says we're not logged in
    /// (another client took the admin login over), logs in again once with
    /// the remembered password and resends.
    /// </summary>
    public async Task<string> SendCommandAsync(string commandLine, bool expectsDirectReply = false)
    {
        await _sendGate.WaitAsync();
        try
        {
            if (!_group.IsLeaderConnected)
            {
                return "Not connected to the server.";
            }

            var replies = await SendAndCollectAsync(commandLine, expectsDirectReply);
            if (replies.Any(r => r.Kind == ServerReplyKind.CommandResult && r.Text.Contains("must first login", StringComparison.OrdinalIgnoreCase)))
            {
                IsLoggedIn = false;
                if (_password is null)
                {
                    return "Not logged in as admin.";
                }

                AddInfo("The admin login was taken over by another client - logging in again.");
                var login = await LoginCoreAsync(_password);
                if (login != AdminLoginResult.Success)
                {
                    _password = null;
                    return $"Admin login lost, logging in again failed: {DescribeLoginFailure(login)}";
                }

                replies = await SendAndCollectAsync(commandLine, expectsDirectReply);
            }

            var answer = replies
                .Where(r => r.Kind != ServerReplyKind.CommandResult)
                .Select(r => r.Text)
                .ToList();
            return answer.Count > 0
                ? string.Join(Environment.NewLine, answer)
                : "Sent. The server didn't reply - successful admin actions are usually only announced in the event log.";
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<List<(ServerReplyKind Kind, string Text)>> SendAndCollectAsync(string commandLine, bool expectsDirectReply)
    {
        var collected = new List<(ServerReplyKind, string)>();
        var directReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _collectedReplies = collected;
        _pendingDirectReply = expectsDirectReply ? directReply : null;
        try
        {
            await _connectionManager.SendMessageAsync(_group, commandLine);
            if (expectsDirectReply)
            {
                await Task.WhenAny(directReply.Task, Task.Delay(LoginTimeout));
            }
            else
            {
                await Task.Delay(ReplyWindow);
            }

            return collected;
        }
        finally
        {
            _collectedReplies = null;
            _pendingDirectReply = null;
        }
    }

    private void OnServerReplyReceived(GroupViewModel group, ServerReplyKind kind, string text)
    {
        if (group != _group)
        {
            return;
        }

        if (_pendingLogin is { } login && kind == ServerReplyKind.CommandResult)
        {
            if (text.Contains("Login successful", StringComparison.OrdinalIgnoreCase))
            {
                login.TrySetResult(AdminLoginResult.Success);
            }
            else if (text.Contains("Password incorrect", StringComparison.OrdinalIgnoreCase))
            {
                login.TrySetResult(AdminLoginResult.WrongPassword);
            }
            else if (text.Contains("Remote administration is disabled", StringComparison.OrdinalIgnoreCase))
            {
                login.TrySetResult(AdminLoginResult.RemoteAdminDisabled);
            }

            return;
        }

        _collectedReplies?.Add((kind, text));
        if (kind is ServerReplyKind.AdminCommandResult or ServerReplyKind.CommandResult)
        {
            _pendingDirectReply?.TrySetResult(true);
        }
    }

    public Task<string> SendItemAsync(AdminPlayerRowViewModel player, string itemName, int amount) =>
        SendCommandAsync(AdminCommands.SendItem(player.Name, itemName, amount));

    public Task<string> SendLocationAsync(AdminPlayerRowViewModel player, string locationName) =>
        SendCommandAsync(AdminCommands.SendLocation(player.Name, locationName));

    public Task<string> ReleaseAsync(AdminPlayerRowViewModel player) =>
        SendCommandAsync(AdminCommands.Release(player.Name));

    public Task<string> CollectAsync(AdminPlayerRowViewModel player) =>
        SendCommandAsync(AdminCommands.Collect(player.Name));

    public Task<string> SetOptionAsync(string option, string value) =>
        SendCommandAsync(AdminCommands.SetOption(option, value), expectsDirectReply: true);

    public Task<GameDataNames?> GetGameDataAsync(string game) => _connectionManager.GetGameDataAsync(_group, game);

    public RoomSettingsSnapshot? GetRoomSettings() => _connectionManager.GetRoomSettings(_group);

    // ── Player list ──

    [RelayCommand]
    private void SetSortByInactivity() => SortByInactivity = true;

    [RelayCommand]
    private void SetSortByName() => SortByInactivity = false;

    partial void OnSortByInactivityChanged(bool value) => RebuildVisiblePlayers();

    /// <summary>
    /// Room roster from the live connection, joined with the tracker's
    /// per-player status/checks/activity by (team, slot). The tracker service
    /// itself never fetches more often than its documented cache timers.
    /// </summary>
    [RelayCommand]
    public async Task RefreshPlayersAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var roster = await _connectionManager.GetRoomPlayersAsync(_group);

            RoomProgressSnapshot? snapshot = null;
            if (_trackerService is not null && _group.HasMultiworldTracker)
            {
                snapshot = await _trackerService.GetProgressAsync(_group.Group.TrackerId!);
                if (snapshot is not null)
                {
                    TrackerFetchedAt = _clock();
                }
            }

            RefreshError = _group.HasMultiworldTracker && snapshot is null
                ? "Could not fetch tracker data right now - showing names and games only."
                : null;

            var progressByPlayer = (snapshot?.Players ?? Array.Empty<PlayerProgress>())
                .GroupBy(p => (p.Team, p.Player))
                .ToDictionary(g => g.Key, g => g.First());

            _allPlayers.Clear();
            foreach (var player in roster)
            {
                progressByPlayer.TryGetValue((player.Team, player.Slot), out var progress);
                _allPlayers.Add(new AdminPlayerRowViewModel(_clock, progress?.CheckedLocationIds)
                {
                    Team = player.Team,
                    Slot = player.Slot,
                    Name = player.Name,
                    Alias = player.Alias,
                    Game = player.Game ?? string.Empty,
                    IsOwnSlot = _group.Group.Slots.Any(s => string.Equals(s.SlotName, player.Name, StringComparison.OrdinalIgnoreCase)),
                    HasTrackerData = progress is not null,
                    InitialChecksDone = progress?.ChecksDone ?? 0,
                    ChecksTotal = progress?.ChecksTotal,
                    LastActivity = progress?.LastActivity,
                    ClientStatus = progress?.ClientStatus,
                });
            }

            RebuildVisiblePlayers();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RebuildVisiblePlayers()
    {
        var visible = _allPlayers.Where(p => !(ExcludeGoaled && p.IsGoal));
        var sorted = SortByInactivity
            ? visible.OrderByDescending(p => p.HasTrackerData ? p.InactiveSortKey : TimeSpan.MinValue)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            : visible.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

        Players.Clear();
        foreach (var player in sorted)
        {
            Players.Add(player);
        }
    }

    private void OnTick()
    {
        foreach (var player in Players)
        {
            player.RefreshInactivity();
        }

        OnPropertyChanged(nameof(TrackerAgeText));
    }

    private void AddInfo(string text) =>
        _group.Events.Add(new EventEntry { Type = EventType.Admin, Text = $"[Archipolygo] {text}" });
}
