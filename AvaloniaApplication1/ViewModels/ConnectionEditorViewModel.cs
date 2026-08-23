using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
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
    /// shown read-only; slots are picked from the room's player roster
    /// rather than typed, and several can be queued up (see
    /// <see cref="ConnectionEditorViewModel.StagedSlots"/>) and added
    /// together in one go instead of one dialog round-trip each.
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

    /// <summary>
    /// <see cref="ConnectionEditorMode.AddSlot"/> only: every slot to add in
    /// this one go, each with its own optional per-slot password override
    /// (see <see cref="StagedSlot.Password"/>). Includes both explicitly
    /// staged entries and - if one was picked but never staged - the one
    /// currently selected in the picker, so confirming after choosing just a
    /// single slot still works without ever touching "Add to list". Empty
    /// for every other mode.
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
    /// The room's shared password (<see cref="ConnectionEditorMode.NewGroup"/>/<see cref="ConnectionEditorMode.EditGroup"/>),
    /// or - for <see cref="ConnectionEditorMode.AddSlot"/> - the per-slot
    /// override password for whichever player is currently picked in
    /// <see cref="SelectedPlayer"/>, left empty to just use the group's
    /// existing password. Cleared after
    /// each <see cref="StageSelectedPlayer"/> so it starts fresh for the
    /// next slot.
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// The slot currently picked from <see cref="AvailablePlayers"/> - only
    /// relevant for <see cref="ConnectionEditorMode.AddSlot"/>. Not yet
    /// queued to be added until either <see cref="StageSelectedPlayer"/> is
    /// called, or the dialog is confirmed with this still set (see
    /// <see cref="TryBuildResult"/>), which folds it in as if it had been
    /// staged.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StageSelectedPlayerCommand))]
    private PlayerChoice? _selectedPlayer;

    /// <summary>
    /// Room players not yet configured as a slot on this server and not
    /// already staged, for the "Add slot" dialog's picker. Populated by
    /// <see cref="ForAddSlot"/>; empty (with <see cref="ValidationError"/>
    /// explaining why) outside <see cref="ConnectionEditorMode.AddSlot"/> or
    /// if the roster couldn't be fetched.
    /// </summary>
    public ObservableCollection<PlayerChoice> AvailablePlayers { get; } = new();

    /// <summary>
    /// Slots queued up to add together when the dialog is confirmed - see
    /// <see cref="StageSelectedPlayer"/>/<see cref="UnstageSlot"/>. Lets
    /// several slots (each possibly with its own password override) be
    /// picked in one dialog visit instead of one "Add slot" round-trip per
    /// slot.
    /// </summary>
    public ObservableCollection<StagedSlot> StagedSlots { get; } = new();

    /// <summary>
    /// <see cref="ConnectionEditorMode.EditGroup"/> only: every slot already
    /// configured on this server, for the slot-management list (default
    /// leader + remove per row) - see <see cref="ForEditGroup"/>,
    /// <see cref="MakeDefaultLeader"/> and <see cref="RemoveConfiguredSlotAsync"/>.
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
    /// Removing a configured slot has a real side effect (disconnecting a
    /// live leader) that this lightweight dialog view model has no way to
    /// perform itself, so it's delegated to whoever opened the dialog (see
    /// <see cref="ForEditGroup"/>) - unlike every other change here, this
    /// takes effect immediately when clicked rather than waiting for Save,
    /// since there's no clean way to "undo" a disconnect on Cancel.
    /// </summary>
    private Func<SlotProfile, Task>? _removeSlotAsync;

    public ConnectionEditorMode Mode { get; private init; }

    /// <summary>The slot-management list (default leader + remove) is only relevant when editing an already-existing server.</summary>
    public bool ShowSlotManagement => Mode == ConnectionEditorMode.EditGroup;

    /// <summary>Host/Port are fixed (shown read-only) when adding a slot to an already-existing server.</summary>
    public bool IsServerReadOnly => Mode == ConnectionEditorMode.AddSlot;

    /// <summary>The free-text slot name field is only shown when creating a brand-new server.</summary>
    public bool ShowSlotName => Mode == ConnectionEditorMode.NewGroup;

    /// <summary>The room-roster picker (and its staging list) is only shown when adding slots to an already-existing server.</summary>
    public bool ShowPlayerPicker => Mode == ConnectionEditorMode.AddSlot;

    /// <summary>Auto-connect is a server-level setting; not relevant when only adding slots to one.</summary>
    public bool ShowAutoConnect => Mode != ConnectionEditorMode.AddSlot;

    public string DialogTitle => Mode switch
    {
        ConnectionEditorMode.NewGroup => "New Server",
        ConnectionEditorMode.AddSlot => "Add Slot",
        ConnectionEditorMode.EditGroup => "Edit Server",
        _ => "Connection"
    };

    /// <summary>
    /// Reflects how many slots would actually be added right now (staged
    /// entries, plus the currently picked-but-not-yet-staged one, if any) -
    /// visible feedback that queuing up several slots before confirming
    /// actually did something, since the dialog otherwise looks unchanged
    /// after each "Add to list" click.
    /// </summary>
    public string SaveButtonText
    {
        get
        {
            if (Mode != ConnectionEditorMode.AddSlot)
            {
                return "Save";
            }

            var pending = StagedSlots.Count + (SelectedPlayer is null ? 0 : 1);
            return pending <= 1 ? "Add slot" : $"Add {pending} slots";
        }
    }

    /// <summary>Whether the queued-slots list has anything in it - for the view to show/hide it (see <see cref="SaveButtonText"/> for the count itself).</summary>
    public bool HasStagedSlots => StagedSlots.Count > 0;

    /// <summary>Every currently configured server, used for duplicate checks.</summary>
    private IReadOnlyList<ServerConnectionGroup> _existingGroups = Array.Empty<ServerConnectionGroup>();

    /// <summary>The server being added to/edited, for <see cref="ConnectionEditorMode.AddSlot"/>/<see cref="ConnectionEditorMode.EditGroup"/>.</summary>
    private ServerConnectionGroup? _targetGroup;

    public ConnectionEditorViewModel()
    {
        StagedSlots.CollectionChanged += OnStagedSlotsChanged;
    }

    private void OnStagedSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(HasStagedSlots));
    }

    partial void OnSelectedPlayerChanged(PlayerChoice? value) => OnPropertyChanged(nameof(SaveButtonText));

    public static ConnectionEditorViewModel ForNewGroup(bool defaultAutoConnect = false, IReadOnlyList<ServerConnectionGroup>? existingGroups = null) => new()
    {
        Mode = ConnectionEditorMode.NewGroup,
        AutoConnect = defaultAutoConnect,
        _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>()
    };

    /// <summary>
    /// <paramref name="availablePlayers"/> should already be filtered down to
    /// players not yet configured as a slot on <paramref name="group"/> (see
    /// <see cref="MainWindowViewModel.GetAvailableSlotsToAddAsync"/>). An
    /// empty list is shown with an explanatory <see cref="ValidationError"/>
    /// rather than falling back to free-text entry. The first available
    /// player is preselected so confirming the dialog for a single slot
    /// works without touching the picker at all.
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
            viewModel.AvailablePlayers.Add(player);
        }

        viewModel.SelectedPlayer = viewModel.AvailablePlayers.FirstOrDefault();

        if (availablePlayers.Count == 0)
        {
            viewModel.ValidationError = "No players found in the room - is the server reachable, and does this group have at least one working slot?";
        }

        return viewModel;
    }

    /// <param name="removeSlotAsync">
    /// Called (by <see cref="RemoveConfiguredSlotAsync"/>) with the slot the
    /// user clicked "✕" on, to actually remove it - including disconnecting
    /// it first if it's the current leader - since this lightweight dialog
    /// view model has no <c>IConnectionManager</c>/<c>MainWindowViewModel</c>
    /// of its own to do that with.
    /// </param>
    public static ConnectionEditorViewModel ForEditGroup(
        ServerConnectionGroup group,
        Func<SlotProfile, Task> removeSlotAsync,
        IReadOnlyList<ServerConnectionGroup>? existingGroups = null)
    {
        var viewModel = new ConnectionEditorViewModel
        {
            Mode = ConnectionEditorMode.EditGroup,
            Name = group.Name,
            HostPortInput = group.HostPort,
            Password = group.Password,
            AutoConnect = group.AutoConnect,
            PreferredLeaderSlotId = group.PreferredLeaderSlotId,
            _targetGroup = group,
            _removeSlotAsync = removeSlotAsync,
            _existingGroups = existingGroups ?? Array.Empty<ServerConnectionGroup>()
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

    /// <summary>
    /// Moves <see cref="SelectedPlayer"/> (with whatever's currently in
    /// <see cref="Password"/> as its override) into <see cref="StagedSlots"/>,
    /// removes it from the remaining <see cref="AvailablePlayers"/>, and
    /// resets the picker/password so the next slot starts fresh. No-op if
    /// nothing is picked.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStageSelectedPlayer))]
    private void StageSelectedPlayer()
    {
        if (SelectedPlayer is null)
        {
            return;
        }

        StagedSlots.Add(new StagedSlot
        {
            SlotName = SelectedPlayer.SlotName,
            DisplayText = SelectedPlayer.DisplayText,
            Password = string.IsNullOrWhiteSpace(Password) ? null : Password.Trim()
        });

        AvailablePlayers.Remove(SelectedPlayer);
        SelectedPlayer = AvailablePlayers.FirstOrDefault();
        Password = string.Empty;
    }

    private bool CanStageSelectedPlayer() => SelectedPlayer is not null;

    /// <summary>Removes a queued slot again (e.g. staged by mistake), putting the player back into the picker.</summary>
    [RelayCommand]
    private void UnstageSlot(StagedSlot slot)
    {
        StagedSlots.Remove(slot);
        AvailablePlayers.Add(new PlayerChoice { SlotName = slot.SlotName, DisplayText = slot.DisplayText });
    }

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
    /// Actually removes a configured slot right away (see <see cref="_removeSlotAsync"/>)
    /// and drops its row from the list. If the removed slot was the default
    /// leader, that preference is cleared too rather than silently pointing
    /// at a slot that no longer exists.
    /// </summary>
    [RelayCommand]
    private async Task RemoveConfiguredSlotAsync(ConfiguredSlotRow row)
    {
        if (_removeSlotAsync is null)
        {
            return;
        }

        await _removeSlotAsync(row.Slot);
        ConfiguredSlotRows.Remove(row);

        if (row.IsDefaultLeader)
        {
            PreferredLeaderSlotId = null;
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
            // Whatever's still picked-but-not-staged counts too, so
            // confirming right after picking a single slot works without
            // ever touching "Add to list".
            var entries = StagedSlots.ToList();
            if (SelectedPlayer is not null)
            {
                entries.Add(new StagedSlot
                {
                    SlotName = SelectedPlayer.SlotName,
                    DisplayText = SelectedPlayer.DisplayText,
                    Password = string.IsNullOrWhiteSpace(Password) ? null : Password.Trim()
                });
            }

            if (entries.Count == 0)
            {
                ValidationError = "Please select at least one slot.";
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
            PreferredLeaderSlotId = PreferredLeaderSlotId
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
