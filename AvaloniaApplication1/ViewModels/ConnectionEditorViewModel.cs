using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>Which action <see cref="ConnectionEditorViewModel"/> is set up to perform - see its factory methods.</summary>
public enum ConnectionEditorMode
{
    /// <summary>Create a brand-new server (<see cref="ServerConnectionGroup"/>) with its first slot.</summary>
    NewGroup,

    /// <summary>
    /// Add one or more slots to an already-existing server. Host/Port are
    /// shown read-only; slots are picked via a searchable, checkable list of
    /// the room's player roster (see
    /// <see cref="ConnectionEditorViewModel.FilteredSlotRows"/>) rather than
    /// typed, so several can be added together in one go instead of one
    /// dialog round-trip each.
    /// </summary>
    AddSlot,

    /// <summary>
    /// Edit an existing server's Name/Host/Port/Password/AutoConnect, and
    /// manage its already-configured slots: pick which one is the default
    /// leader, or remove one entirely (see <see cref="ConnectionEditorViewModel.ConfiguredSlotRows"/>).
    /// </summary>
    EditGroup
}

/// <summary>Validated output of <see cref="ConnectionEditorViewModel.TryBuildResult"/>, for the view's code-behind to apply.</summary>
public class ConnectionEditorResult
{
    public required ConnectionEditorMode Mode { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Password { get; init; } = string.Empty;

    /// <summary><see cref="ConnectionEditorMode.NewGroup"/> only: the free-typed name of the server's first slot.</summary>
    public string SlotName { get; init; } = string.Empty;

    public bool AutoConnect { get; init; }

    /// <summary>Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md: exactly what the user typed - see <see cref="Models.ServerConnectionGroup.TrackerReferenceInput"/>.</summary>
    public string? TrackerReferenceInput { get; init; }

    /// <summary>The resolved tracker id, if any - see <see cref="Models.ServerConnectionGroup.TrackerId"/> and <see cref="ConnectionEditorViewModel.TryResolveTrackerReferenceAsync"/>.</summary>
    public string? TrackerId { get; init; }

    /// <summary>
    /// <see cref="ConnectionEditorMode.AddSlot"/> only: every checked slot in
    /// the picker, each with its own optional per-slot password override
    /// (see <see cref="StagedSlot.Password"/>) - see
    /// <see cref="ConnectionEditorViewModel.BuildSlotsToAdd"/>. Empty for
    /// every other mode.
    /// </summary>
    public IReadOnlyList<StagedSlot> SlotsToAdd { get; init; } = Array.Empty<StagedSlot>();

    /// <summary>
    /// <see cref="ConnectionEditorMode.EditGroup"/> only: the slot that
    /// should become the leader automatically at startup (see
    /// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>), as picked
    /// via "Make default" in the slot-management list, or null if none/not
    /// applicable.
    /// </summary>
    public Guid? PreferredLeaderSlotId { get; init; }

    /// <summary>
    /// <see cref="ConnectionEditorMode.EditGroup"/> only: every slot the user
    /// clicked "✕" on in the slot-management list during this dialog session -
    /// see <see cref="ConnectionEditorViewModel.RemoveConfiguredSlot"/>.
    /// Staged, not applied until Save: clicking "✕" used to remove a slot
    /// (and disconnect it, if it was the live leader) immediately, which
    /// meant "make a different slot the default, then remove the old
    /// leader" could disconnect the user mid-edit, before they'd even
    /// clicked Save. Applying every removal here instead, in one batch, once
    /// the whole edit is confirmed, means Cancel now actually cancels a
    /// removal too - not just every other field in this dialog.
    /// </summary>
    public IReadOnlyList<SlotProfile> SlotsToRemove { get; init; } = Array.Empty<SlotProfile>();
}

/// <summary>
/// Editor view model for creating a new server, adding one or more slots to
/// an existing one, or editing an existing server's connection details.
/// Shown as a dialog by <see cref="Views.ConnectionEditorWindow"/>; which
/// fields are actually editable in that view depends on <see cref="Mode"/>.
/// </summary>
public partial class ConnectionEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Host and port combined into one "host:port" field, so the whole
    /// address can be copy/pasted in one piece instead of being split across
    /// a text box and a numeric field. Parsed back into Host/Port by
    /// <see cref="TryBuildResult"/>.
    /// </summary>
    [ObservableProperty]
    private string _hostPortInput = "archipelago.gg:38281";

    /// <summary>Free-text slot name - only used for <see cref="ConnectionEditorMode.NewGroup"/>, where no room roster is available yet to pick from.</summary>
    [ObservableProperty]
    private string _slotName = string.Empty;

    /// <summary>
    /// The room's shared password - only relevant for
    /// <see cref="ConnectionEditorMode.NewGroup"/>/<see cref="ConnectionEditorMode.EditGroup"/>.
    /// <see cref="ConnectionEditorMode.AddSlot"/> doesn't use this field at
    /// all: the group's shared password was already entered when its leader
    /// first connected, before any slot picking happens here - each row in
    /// the picker instead carries its own optional override (see
    /// <see cref="Models.SelectableSlotRow.OverridePassword"/>).
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _autoConnect;

    /// <summary>
    /// Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md: the free-text field
    /// accepting a bare tracker id, a tracker URL, or a room URL - see
    /// <see cref="Services.TrackerReferenceParser"/>. Resolved into an actual
    /// tracker id (see <see cref="Models.ServerConnectionGroup.TrackerId"/>)
    /// by <see cref="TryResolveTrackerReferenceAsync"/>, which the view's
    /// Save handler awaits before calling <see cref="TryBuildResult"/>.
    /// </summary>
    [ObservableProperty]
    private string _trackerReferenceInput = string.Empty;

    /// <summary>Whether a room-URL resolution (a <c>/room_status/...</c> call, via <see cref="_resolveTrackerId"/>) is currently in flight - lets the view show a brief loading state instead of looking stuck.</summary>
    [ObservableProperty]
    private bool _isResolvingTracker;

    /// <summary>
    /// Resolves a room id into a tracker id (a <c>/room_status/&lt;id&gt;</c>
    /// call) - delegated to whoever opened the dialog (see
    /// <see cref="ForNewGroup"/>/<see cref="ForEditGroup"/>), since this
    /// lightweight dialog view model has no <c>IMultiworldTrackerService</c>
    /// of its own. Null in every mode/call site that doesn't wire one up, in
    /// which case a room URL simply can't be resolved here (see
    /// <see cref="TryResolveTrackerReferenceAsync"/>).
    /// </summary>
    private Func<string, Task<string?>>? _resolveTrackerId;

    /// <summary>Set once <see cref="TryResolveTrackerReferenceAsync"/> has run successfully - what actually flows into <see cref="ConnectionEditorResult.TrackerId"/>.</summary>
    private string? _resolvedTrackerId;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>Free-text filter over <see cref="SelectableSlotRow.Player"/>'s name/display text - see <see cref="RefreshFilter"/>.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Every room player not yet configured as a slot on this server,
    /// wrapped as a checkbox row - populated once by <see cref="ForAddSlot"/>
    /// and never re-ordered/removed afterwards (checking a row doesn't take
    /// it out of the list, unlike the old stage-then-remove ComboBox flow).
    /// </summary>
    private readonly List<SelectableSlotRow> _allSlotRows = new();

    /// <summary>The subset of <see cref="_allSlotRows"/> matching <see cref="SearchText"/> right now - what the checkbox list actually shows, for the "Add slot" dialog's picker.</summary>
    public ObservableCollection<SelectableSlotRow> FilteredSlotRows { get; } = new();

    /// <summary>How many rows across the whole (unfiltered) roster are currently checked - drives <see cref="CanApplyNow"/>/<see cref="SaveButtonText"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyNow))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int _selectedSlotCount;

    /// <summary>Whether the filtered list has nothing to show right now (distinct from <see cref="ValidationError"/>, which covers the room having no available players at all).</summary>
    public bool HasNoFilteredResults => FilteredSlotRows.Count == 0 && _allSlotRows.Count > 0;

    /// <summary>Mirrors the design's "Select at least one slot to continue" rule - Save stays disabled for <see cref="ConnectionEditorMode.AddSlot"/> until at least one row is checked; irrelevant (always true) for every other mode.</summary>
    public bool CanApplyNow => Mode != ConnectionEditorMode.AddSlot || SelectedSlotCount > 0;

    /// <summary>
    /// <see cref="ConnectionEditorMode.EditGroup"/> only: every slot already
    /// configured on this server, for the slot-management list (default
    /// leader + remove per row) - see <see cref="ForEditGroup"/>,
    /// <see cref="MakeDefaultLeader"/> and <see cref="RemoveConfiguredSlot"/>.
    /// </summary>
    public ObservableCollection<ConfiguredSlotRow> ConfiguredSlotRows { get; } = new();

    /// <summary>
    /// The slot that should become the leader automatically at startup (see
    /// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>) - which row
    /// in <see cref="ConfiguredSlotRows"/> has <c>IsDefaultLeader</c> set,
    /// mirrored here so it flows out through <see cref="TryBuildResult"/>.
    /// </summary>
    [ObservableProperty]
    private Guid? _preferredLeaderSlotId;

    /// <summary>
    /// Every slot clicked "✕" on so far this dialog session - see
    /// <see cref="RemoveConfiguredSlot"/> and <see cref="ConnectionEditorResult.SlotsToRemove"/>.
    /// Removing a configured slot has a real side effect (disconnecting a
    /// live leader, if it's the current one) that this lightweight dialog
    /// view model has no way to perform itself - unlike the old immediate-
    /// removal design, though, that side effect is deliberately deferred to
    /// Save (see <see cref="MainWindowViewModel.UpdateGroup"/>) rather than
    /// applied the moment "✕" is clicked, so a same-session "make a
    /// different slot the default, then remove the old leader" can no
    /// longer disconnect the user mid-edit before they've even saved.
    /// </summary>
    private readonly List<SlotProfile> _slotsToRemove = new();

    public ConnectionEditorMode Mode { get; private init; }

    /// <summary>The slot-management list (default leader + remove) is only relevant when editing an already-existing server.</summary>
    public bool ShowSlotManagement => Mode == ConnectionEditorMode.EditGroup;

    /// <summary>Host/Port are fixed (shown read-only) when adding a slot to an already-existing server.</summary>
    public bool IsServerReadOnly => Mode == ConnectionEditorMode.AddSlot;

    /// <summary>The free-text slot name field is only shown when creating a brand-new server.</summary>
    public bool ShowSlotName => Mode == ConnectionEditorMode.NewGroup;

    /// <summary>The searchable room-roster checkbox picker is only shown when adding slots to an already-existing server.</summary>
    public bool ShowPlayerPicker => Mode == ConnectionEditorMode.AddSlot;

    /// <summary>Auto-connect is a server-level setting; not relevant when only adding slots to one.</summary>
    public bool ShowAutoConnect => Mode != ConnectionEditorMode.AddSlot;

    /// <summary>Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md is a server-level setting too, same reasoning as <see cref="ShowAutoConnect"/> - not relevant when only adding slots to an existing server.</summary>
    public bool ShowMultiworldTracker => Mode != ConnectionEditorMode.AddSlot;

    public string DialogTitle => Mode switch
    {
        ConnectionEditorMode.NewGroup => "New Server",
        ConnectionEditorMode.AddSlot => "Add Slot",
        ConnectionEditorMode.EditGroup => "Edit Server",
        _ => "Connection"
    };

    /// <summary>Reflects how many slots are currently checked - visible feedback that ticking boxes actually did something, since the dialog otherwise looks unchanged.</summary>
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

    /// <summary>Every currently configured server, used for duplicate checks.</summary>
    private IReadOnlyList<ServerConnectionGroup> _existingGroups = Array.Empty<ServerConnectionGroup>();

    /// <summary>The server being added to/edited, for <see cref="ConnectionEditorMode.AddSlot"/>/<see cref="ConnectionEditorMode.EditGroup"/>.</summary>
    private ServerConnectionGroup? _targetGroup;

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    public static ConnectionEditorViewModel ForNewGroup(
        bool defaultAutoConnect = false,
        IReadOnlyList<ServerConnectionGroup>? existingGroups = null,
        Func<string, Task<string?>>? resolveTrackerId = null) => new()
    {
        Mode = ConnectionEditorMode.NewGroup,
        AutoConnect = defaultAutoConnect,
        _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>(),
        _resolveTrackerId = resolveTrackerId
    };

    /// <summary>
    /// <paramref name="availablePlayers"/> should already be filtered down to
    /// players not yet configured as a slot on <paramref name="group"/> (see
    /// <see cref="MainWindowViewModel.GetAvailableSlotsToAddAsync"/>). An
    /// empty list is shown with an explanatory <see cref="ValidationError"/>
    /// rather than falling back to free-text entry. Nothing is preselected -
    /// Save stays disabled (<see cref="CanApplyNow"/>) until at least one row
    /// is checked.
    /// </summary>
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
        Func<string, Task<string?>>? resolveTrackerId = null)
    {
        var viewModel = new ConnectionEditorViewModel
        {
            Mode = ConnectionEditorMode.EditGroup,
            Name = group.Name,
            HostPortInput = group.HostPort,
            Password = group.Password,
            AutoConnect = group.AutoConnect,
            PreferredLeaderSlotId = group.PreferredLeaderSlotId,
            TrackerReferenceInput = group.TrackerReferenceInput ?? string.Empty,
            _targetGroup = group,
            _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>(),
            _resolveTrackerId = resolveTrackerId,
            _resolvedTrackerId = group.TrackerId
        };

        // Default leader first, then alphabetical - same ordering rule as
        // GroupViewModel.RefreshSlotOrder's "Chat as"/slot-filter lists,
        // applied here to the default-leader concept instead of the
        // currently-connected one, since this dialog manages the server's
        // configuration rather than a live connection.
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

    /// <summary>Orders slots with <paramref name="leaderSlotId"/>'s slot (if any) first, then the rest alphabetically by name.</summary>
    private static IEnumerable<SlotProfile> SortSlotsForDisplay(IEnumerable<SlotProfile> slots, Guid? leaderSlotId) =>
        slots
            .OrderBy(s => s.Id == leaderSlotId ? 0 : 1)
            .ThenBy(s => s.SlotName, StringComparer.OrdinalIgnoreCase);

    private void OnSlotRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableSlotRow.IsSelected))
        {
            SelectedSlotCount = _allSlotRows.Count(r => r.IsSelected);
        }
    }

    /// <summary>Rebuilds <see cref="FilteredSlotRows"/> from <see cref="_allSlotRows"/> by name/display text.</summary>
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

    /// <summary>Checks every row currently shown by the filter.</summary>
    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var row in FilteredSlotRows)
        {
            row.IsSelected = true;
        }
    }

    /// <summary>Unchecks every row currently shown by the filter.</summary>
    [RelayCommand]
    private void DeselectVisible()
    {
        foreach (var row in FilteredSlotRows)
        {
            row.IsSelected = false;
        }
    }

    /// <summary>
    /// Every checked row, turned into a <see cref="StagedSlot"/> - its own
    /// override (if typed) wins, else null, meaning "fall back to the
    /// group's own shared password" (see <see cref="Password"/>'s doc
    /// comment for why there's no separate batch-wide password here).
    /// </summary>
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

    /// <summary>
    /// Marks one row as the server's default/preferred leader, un-marking
    /// whichever row had it before, and moves it to the front of the list
    /// to match (see <see cref="SortSlotsForDisplay"/>).
    /// </summary>
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

    /// <summary>
    /// Stages a configured slot for removal (see <see cref="_slotsToRemove"/>)
    /// and drops its row from the list right away, so the dialog reflects
    /// the pending change - but the actual removal (and any disconnect it
    /// triggers, if this was the live leader) only happens on Save, via
    /// <see cref="ConnectionEditorResult.SlotsToRemove"/>. If the removed
    /// slot was the default leader, that preference is cleared too rather
    /// than silently pointing at a slot that's about to no longer exist.
    /// </summary>
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

    /// <summary>
    /// Resolves <see cref="TrackerReferenceInput"/> (Tier 2 of Feature-Plaene/Archiv/Fortschrittsanzeigen.md)
    /// into <see cref="_resolvedTrackerId"/>, which <see cref="TryBuildResult"/>
    /// then reads. Must be awaited by the view's Save handler *before* calling
    /// <see cref="TryBuildResult"/>, since resolving a room URL needs a
    /// network round-trip that plain synchronous validation can't do. An
    /// empty field is not an error - it just means Tier 2 stays disabled for
    /// this group. Sets <see cref="ValidationError"/> and returns false on
    /// any failure, same convention as <see cref="TryBuildResult"/>.
    /// </summary>
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

    /// <summary>Validates the input for the current <see cref="Mode"/> and returns a result on success.</summary>
    public bool TryBuildResult(out ConnectionEditorResult result)
    {
        result = null!;

        string host;
        int port;

        if (Mode == ConnectionEditorMode.AddSlot)
        {
            // Host/Port aren't editable here - reuse the target group's own values verbatim.
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
            SlotsToAdd = slotsToAdd,
            PreferredLeaderSlotId = PreferredLeaderSlotId,
            SlotsToRemove = _slotsToRemove,
            TrackerReferenceInput = ShowMultiworldTracker ? TrackerReferenceInput.Trim() : null,
            TrackerId = ShowMultiworldTracker ? _resolvedTrackerId : null
        };
        return true;
    }

    /// <summary>
    /// Splits "host:port" on the last colon (so a bare hostname/IPv4 host
    /// works the same as before); rejects anything that isn't a valid
    /// 1-65535 port or has an empty host part.
    /// </summary>
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
