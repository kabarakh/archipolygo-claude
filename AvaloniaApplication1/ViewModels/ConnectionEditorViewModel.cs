using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

public enum ConnectionEditorMode
{
    NewGroup,

    /// <summary>Slots are picked from the room roster (several at once), Host/Port read-only.</summary>
    AddSlot,

    /// <summary>Also manages configured slots: default leader, remove.</summary>
    EditGroup
}

public class ConnectionEditorResult
{
    public required ConnectionEditorMode Mode { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Password { get; init; } = string.Empty;

    public string SlotName { get; init; } = string.Empty;

    public bool AutoConnect { get; init; }

    public bool NotificationsMuted { get; init; }

    public bool IsAdmin { get; init; }

    public string? TrackerReferenceInput { get; init; }

    public string? TrackerId { get; init; }

    public IReadOnlyList<StagedSlot> SlotsToAdd { get; init; } = Array.Empty<StagedSlot>();

    public Guid? PreferredLeaderSlotId { get; init; }

    /// <summary>
    /// Removals are staged until Save, so "make another slot default, then remove the old leader" can't disconnect
    /// the user mid-edit, and Cancel also cancels removals.
    /// </summary>
    public IReadOnlyList<SlotProfile> SlotsToRemove { get; init; } = Array.Empty<SlotProfile>();

    /// <summary>Empty for <see cref="ConnectionEditorMode.AddSlot"/>, which doesn't touch the server's color.</summary>
    public string Color { get; init; } = string.Empty;
}

/// <summary>Dialog view model for creating a server, adding slots to one, or editing one - see <see cref="Mode"/>.</summary>
public partial class ConnectionEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// One "host:port" field so the address pastes in one piece. May also hold a room link/id, which
    /// <see cref="TryResolveHostPortAsync"/> replaces with the real "host:port" before <see cref="TryBuildResult"/>.
    /// </summary>
    [ObservableProperty]
    private string _hostPortInput = "archipelago.gg:38281";

    [ObservableProperty]
    private bool _isResolvingHostPort;

    /// <summary>NewGroup only - no room roster is available yet to pick from.</summary>
    [ObservableProperty]
    private string _slotName = string.Empty;

    /// <summary>Unused for AddSlot: the shared password is already known; picker rows carry optional overrides.</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private bool _notificationsMuted;

    [ObservableProperty]
    private bool _isAdmin;

    /// <summary>A bare tracker id, a tracker URL, or a room URL - resolved by <see cref="TryResolveTrackerReferenceAsync"/>.</summary>
    [ObservableProperty]
    private string _trackerReferenceInput = string.Empty;

    [ObservableProperty]
    private bool _isResolvingTracker;

    /// <summary>Room id to tracker id; supplied by the caller since this dialog VM has no tracker service. May be null.</summary>
    private Func<string, Task<string?>>? _resolveTrackerId;

    private string? _resolvedTrackerId;

    /// <summary>Room id to Host/Port; supplied by the caller like <see cref="_resolveTrackerId"/>. May be null.</summary>
    private Func<string, Task<RoomConnectionInfo?>>? _resolveRoomConnectionInfo;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private string _searchText = string.Empty;

    private readonly List<SelectableSlotRow> _allSlotRows = new();

    public ObservableCollection<SelectableSlotRow> FilteredSlotRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyNow))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int _selectedSlotCount;

    /// <summary>Distinct from <see cref="ValidationError"/>, which covers the room having no available players at all.</summary>
    public bool HasNoFilteredResults => FilteredSlotRows.Count == 0 && _allSlotRows.Count > 0;

    public bool CanApplyNow => Mode != ConnectionEditorMode.AddSlot || SelectedSlotCount > 0;

    public ObservableCollection<ConfiguredSlotRow> ConfiguredSlotRows { get; } = new();

    [ObservableProperty]
    private Guid? _preferredLeaderSlotId;

    /// <summary>Applied only on Save - see <see cref="ConnectionEditorResult.SlotsToRemove"/>.</summary>
    private readonly List<SlotProfile> _slotsToRemove = new();

    public ConnectionEditorMode Mode { get; private init; }

    public bool ShowSlotManagement => Mode == ConnectionEditorMode.EditGroup;

    public bool IsServerReadOnly => Mode == ConnectionEditorMode.AddSlot;

    public bool ShowSlotName => Mode == ConnectionEditorMode.NewGroup;

    public bool ShowPlayerPicker => Mode == ConnectionEditorMode.AddSlot;

    public bool ShowAutoConnect => Mode != ConnectionEditorMode.AddSlot;

    /// <summary>Edit only: a brand-new server has nothing to be noisy about yet.</summary>
    public bool ShowNotificationsMuted => Mode == ConnectionEditorMode.EditGroup;

    public bool ShowIsAdmin => Mode != ConnectionEditorMode.AddSlot;

    public bool ShowMultiworldTracker => Mode != ConnectionEditorMode.AddSlot;

    public bool ShowColorPicker => Mode != ConnectionEditorMode.AddSlot;

    [ObservableProperty]
    private Color _selectedColor;

    public string DialogTitle => Mode switch
    {
        ConnectionEditorMode.NewGroup => "New Server",
        ConnectionEditorMode.AddSlot => "Add Slot",
        ConnectionEditorMode.EditGroup => "Edit Server",
        _ => "Connection"
    };

    /// <summary>Shows the checked count - the only visible feedback that ticking boxes did something.</summary>
    public string SaveButtonText
    {
        get
        {
            if (Mode != ConnectionEditorMode.AddSlot)
            {
                return "Save";
            }

            return SelectedSlotCount <= 1 ? "Add slot" : $"Add {SelectedSlotCount} slots";
        }
    }

    private IReadOnlyList<ServerConnectionGroup> _existingGroups = Array.Empty<ServerConnectionGroup>();

    private ServerConnectionGroup? _targetGroup;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public static ConnectionEditorViewModel ForNewGroup(
        bool defaultAutoConnect = false,
        IReadOnlyList<ServerConnectionGroup>? existingGroups = null,
        Func<string, Task<string?>>? resolveTrackerId = null,
        Func<string, Task<RoomConnectionInfo?>>? resolveRoomConnectionInfo = null)
    {
        var viewModel = new ConnectionEditorViewModel
        {
            Mode = ConnectionEditorMode.NewGroup,
            AutoConnect = defaultAutoConnect,
            _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>(),
            _resolveTrackerId = resolveTrackerId,
            _resolveRoomConnectionInfo = resolveRoomConnectionInfo
        };

        viewModel.SelectedColor = ComputeSuggestedColor(viewModel._existingGroups, null);

        return viewModel;
    }

    /// <summary><paramref name="availablePlayers"/> must already exclude players configured on <paramref name="group"/>.</summary>
    public static ConnectionEditorViewModel ForAddSlot(ServerConnectionGroup group, IReadOnlyList<PlayerChoice> availablePlayers)
    {
        var viewModel = new ConnectionEditorViewModel
        {
            Mode = ConnectionEditorMode.AddSlot,
            Name = group.Name,
            HostPortInput = group.HostPort,
            _targetGroup = group
        };

        foreach (var player in availablePlayers)
        {
            var row = new SelectableSlotRow { Player = player };
            row.PropertyChanged += viewModel.OnSlotRowPropertyChanged;
            viewModel._allSlotRows.Add(row);
        }

        viewModel.RefreshFilter();

        if (availablePlayers.Count == 0)
        {
            viewModel.ValidationError = "No players found in the room - is the server reachable, and does this group have at least one working slot?";
        }

        return viewModel;
    }

    public static ConnectionEditorViewModel ForEditGroup(
        ServerConnectionGroup group,
        IReadOnlyList<ServerConnectionGroup>? existingGroups = null,
        Func<string, Task<string?>>? resolveTrackerId = null,
        Func<string, Task<RoomConnectionInfo?>>? resolveRoomConnectionInfo = null)
    {
        var viewModel = new ConnectionEditorViewModel
        {
            Mode = ConnectionEditorMode.EditGroup,
            Name = group.Name,
            HostPortInput = group.HostPort,
            Password = group.Password,
            AutoConnect = group.AutoConnect,
            NotificationsMuted = group.NotificationsMuted,
            IsAdmin = group.IsAdmin,
            PreferredLeaderSlotId = group.PreferredLeaderSlotId,
            TrackerReferenceInput = group.TrackerReferenceInput ?? string.Empty,
            _targetGroup = group,
            _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>(),
            _resolveTrackerId = resolveTrackerId,
            _resolveRoomConnectionInfo = resolveRoomConnectionInfo,
            _resolvedTrackerId = group.TrackerId,
            SelectedColor = ParseColorOrFallback(group.Color)
        };

        // Ordered by the *default* leader, not the live one - this dialog edits configuration.
        foreach (var slot in SortSlotsForDisplay(group.Slots, group.PreferredLeaderSlotId))
        {
            viewModel.ConfiguredSlotRows.Add(new ConfiguredSlotRow
            {
                Slot = slot,
                IsDefaultLeader = slot.Id == group.PreferredLeaderSlotId
            });
        }

        return viewModel;
    }

    private static IEnumerable<SlotProfile> SortSlotsForDisplay(IEnumerable<SlotProfile> slots, Guid? leaderSlotId) =>
        slots
            .OrderBy(s => s.Id == leaderSlotId ? 0 : 1)
            .ThenBy(s => s.SlotName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Excludes <paramref name="targetGroup"/> itself, so its about-to-be-replaced color doesn't skew the suggestion.</summary>
    private static Color ComputeSuggestedColor(IReadOnlyList<ServerConnectionGroup> existingGroups, ServerConnectionGroup? targetGroup) =>
        ParseColorOrFallback(ServerColorPalette.AssignColor(
            existingGroups.Where(g => g != targetGroup).Select(g => g.Color)));

    /// <summary>Never throws into the view; falls back to the palette's first color.</summary>
    private static Color ParseColorOrFallback(string colorString) =>
        Color.TryParse(colorString, out var color) ? color : Color.Parse(ServerColorPalette.Colors[0]);

    private void OnSlotRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableSlotRow.IsSelected))
        {
            SelectedSlotCount = _allSlotRows.Count(r => r.IsSelected);
        }
    }

    private void RefreshFilter()
    {
        var query = SearchText.Trim();
        FilteredSlotRows.Clear();

        foreach (var row in _allSlotRows)
        {
            if (query.Length == 0 ||
                row.Player.SlotName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Player.DisplayText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredSlotRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasNoFilteredResults));
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var row in FilteredSlotRows)
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectVisible()
    {
        foreach (var row in FilteredSlotRows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>A null password means "use the group's shared password".</summary>
    private IReadOnlyList<StagedSlot> BuildSlotsToAdd() =>
        _allSlotRows
            .Where(r => r.IsSelected)
            .Select(r => new StagedSlot
            {
                SlotName = r.Player.SlotName,
                DisplayText = r.Player.DisplayText,
                Password = string.IsNullOrWhiteSpace(r.OverridePassword) ? null : r.OverridePassword!.Trim()
            })
            .ToList();

    [RelayCommand]
    private void MakeDefaultLeader(ConfiguredSlotRow row)
    {
        PreferredLeaderSlotId = row.Slot.Id;

        foreach (var candidate in ConfiguredSlotRows)
        {
            candidate.IsDefaultLeader = candidate == row;
        }

        var reordered = SortSlotsForDisplay(ConfiguredSlotRows.Select(r => r.Slot), PreferredLeaderSlotId)
            .Select(slot => ConfiguredSlotRows.First(r => r.Slot == slot))
            .ToList();

        ConfiguredSlotRows.Clear();
        foreach (var reorderedRow in reordered)
        {
            ConfiguredSlotRows.Add(reorderedRow);
        }
    }

    /// <summary>Only staged; the real removal (and possible leader disconnect) happens on Save.</summary>
    [RelayCommand]
    private void RemoveConfiguredSlot(ConfiguredSlotRow row)
    {
        _slotsToRemove.Add(row.Slot);
        ConfiguredSlotRows.Remove(row);

        if (row.IsDefaultLeader)
        {
            PreferredLeaderSlotId = null;
        }
    }

    /// <summary>A fresh suggestion, not the previous automatic value - no history of that is kept.</summary>
    [RelayCommand]
    private void ResetColorToAutomatic()
    {
        SelectedColor = ComputeSuggestedColor(_existingGroups, _targetGroup);
    }

    /// <summary>
    /// If <see cref="HostPortInput"/> isn't "host:port", resolves it as a room link/id and overwrites it (also filling
    /// an empty tracker field). Await before <see cref="TryResolveTrackerReferenceAsync"/> and <see cref="TryBuildResult"/>.
    /// </summary>
    public async Task<bool> TryResolveHostPortAsync()
    {
        if (Mode == ConnectionEditorMode.AddSlot)
        {
            return true;
        }

        if (TryParseHostPort(HostPortInput, out _, out _))
        {
            return true;
        }

        if (!TryExtractRoomIdForHostPortLookup(HostPortInput, out var roomId, out var isTrackerLink))
        {
            ValidationError = isTrackerLink
                ? "That looks like a tracker link, not a room link - paste the room URL instead, or enter host:port directly."
                : "Server must be either host:port (e.g. archipelago.gg:38281) or a room link/id.";
            return false;
        }

        if (_resolveRoomConnectionInfo is null)
        {
            ValidationError = "Cannot resolve a room link right now - enter host:port directly instead.";
            return false;
        }

        IsResolvingHostPort = true;
        try
        {
            var info = await _resolveRoomConnectionInfo(roomId);
            if (info is null)
            {
                ValidationError = "Could not find that room - check the link, or enter host:port directly if this server has no webhost (e.g. self-hosted).";
                return false;
            }

            ValidationError = null;
            HostPortInput = $"{info.Host}:{info.Port}";

            if (string.IsNullOrWhiteSpace(TrackerReferenceInput) && !string.IsNullOrEmpty(info.TrackerId))
            {
                TrackerReferenceInput = info.TrackerId;
            }

            return true;
        }
        finally
        {
            IsResolvingHostPort = false;
        }
    }

    /// <summary>
    /// Unlike <see cref="TrackerReferenceParser"/>, treats a bare id as a room id - the only guess that can yield
    /// host/port. An explicit tracker link is rejected: a tracker id carries no host/port.
    /// </summary>
    private static bool TryExtractRoomIdForHostPortLookup(string input, out string roomId, out bool isTrackerLink)
    {
        roomId = string.Empty;
        isTrackerLink = false;

        if (!TrackerReferenceParser.TryParseTrackerReference(input, out var kind, out var value))
        {
            return false;
        }

        if (kind == TrackerReferenceKind.RoomId)
        {
            roomId = value;
            return true;
        }

        // The parser reports both "/tracker/<id>" and a bare id as TrackerId; only the bare one is worth trying.
        var trimmed = (input ?? string.Empty).Trim();
        var suffixIndex = trimmed.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
        {
            trimmed = trimmed[..suffixIndex];
        }

        trimmed = trimmed.Trim('/');

        if (trimmed.Contains('/'))
        {
            isTrackerLink = true;
            return false;
        }

        roomId = value;
        return true;
    }

    /// <summary>Await before <see cref="TryBuildResult"/> - a room URL needs a network round-trip. Empty input is not an error.</summary>
    public async Task<bool> TryResolveTrackerReferenceAsync()
    {
        if (!ShowMultiworldTracker)
        {
            _resolvedTrackerId = null;
            return true;
        }

        var input = TrackerReferenceInput.Trim();
        if (input.Length == 0)
        {
            _resolvedTrackerId = null;
            return true;
        }

        if (!TrackerReferenceParser.TryParseTrackerReference(input, out var kind, out var value))
        {
            ValidationError = "Could not recognize this as a tracker id, tracker URL, or room URL.";
            return false;
        }

        if (kind == TrackerReferenceKind.TrackerId)
        {
            ValidationError = null;
            _resolvedTrackerId = value;
            return true;
        }

        if (_resolveTrackerId is null)
        {
            ValidationError = "Cannot resolve a room URL right now.";
            return false;
        }

        IsResolvingTracker = true;
        try
        {
            var resolved = await _resolveTrackerId(value);
            if (resolved is null)
            {
                ValidationError = "Could not find a tracker for that room - check the URL, or that the room is hosted via a webhost.";
                return false;
            }

            ValidationError = null;
            _resolvedTrackerId = resolved;
            return true;
        }
        finally
        {
            IsResolvingTracker = false;
        }
    }

    public bool TryBuildResult(out ConnectionEditorResult result)
    {
        result = null!;

        string host;
        int port;

        if (Mode == ConnectionEditorMode.AddSlot)
        {
            host = _targetGroup!.Host;
            port = _targetGroup.Port;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                ValidationError = "Name must not be empty.";
                return false;
            }

            if (!TryParseHostPort(HostPortInput, out host, out port))
            {
                ValidationError = "Server must be in the form host:port (e.g. archipelago.gg:38281).";
                return false;
            }
        }

        var slotName = string.Empty;
        IReadOnlyList<StagedSlot> slotsToAdd = Array.Empty<StagedSlot>();

        if (Mode == ConnectionEditorMode.NewGroup)
        {
            slotName = SlotName.Trim();

            if (string.IsNullOrWhiteSpace(slotName))
            {
                ValidationError = "Slot name must not be empty.";
                return false;
            }

            var isDuplicate = _existingGroups.Any(g =>
                string.Equals(g.Host, host, StringComparison.OrdinalIgnoreCase) &&
                g.Port == port &&
                g.Slots.Any(s => string.Equals(s.SlotName, slotName, StringComparison.OrdinalIgnoreCase)));

            if (isDuplicate)
            {
                ValidationError = "A slot with that name already exists on that host and port.";
                return false;
            }
        }
        else if (Mode == ConnectionEditorMode.AddSlot)
        {
            var entries = BuildSlotsToAdd();
            if (entries.Count == 0)
            {
                ValidationError = "Select at least one slot to continue.";
                return false;
            }

            slotsToAdd = entries;
        }

        ValidationError = null;
        result = new ConnectionEditorResult
        {
            Mode = Mode,
            Name = Name.Trim(),
            Host = host,
            Port = port,
            Password = Mode == ConnectionEditorMode.AddSlot ? string.Empty : Password,
            SlotName = slotName,
            AutoConnect = AutoConnect,
            NotificationsMuted = ShowNotificationsMuted && NotificationsMuted,
            IsAdmin = ShowIsAdmin && IsAdmin,
            SlotsToAdd = slotsToAdd,
            PreferredLeaderSlotId = PreferredLeaderSlotId,
            SlotsToRemove = _slotsToRemove,
            TrackerReferenceInput = ShowMultiworldTracker ? TrackerReferenceInput.Trim() : null,
            TrackerId = ShowMultiworldTracker ? _resolvedTrackerId : null,
            Color = ShowColorPicker ? SelectedColor.ToString() : string.Empty
        };
        return true;
    }

    private static bool TryParseHostPort(string input, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var trimmed = (input ?? string.Empty).Trim();
        var separatorIndex = trimmed.LastIndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == trimmed.Length - 1)
        {
            return false;
        }

        var hostPart = trimmed[..separatorIndex].Trim();
        var portPart = trimmed[(separatorIndex + 1)..].Trim();

        if (string.IsNullOrEmpty(hostPart) || !int.TryParse(portPart, out port) || port is <= 0 or > 65535)
        {
            return false;
        }

        host = hostPart;
        return true;
    }
}
