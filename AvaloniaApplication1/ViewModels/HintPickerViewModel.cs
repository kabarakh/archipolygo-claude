using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// Backs the "Hint..." picker: a searchable list instead of typing <c>!hint</c>/<c>!hint_location</c> by hand.
/// Item hints still go out as the plain <c>!hint &lt;name&gt;</c> chat command - the client can't resolve an
/// item name to a location id. A non-leader slot's connection is held open while it stays selected, then released.
/// </summary>
public partial class HintPickerViewModel : ObservableObject
{
    private readonly GroupViewModel _group;
    private readonly IConnectionManager _connectionManager;

    /// <summary>The selected slot+mode(+exclusion)'s rows, before the search filter.</summary>
    private List<HintPickerRow> _availableRows = new();

    /// <summary>Lets a slower, earlier fetch detect it's stale and discard its result.</summary>
    private int _requestVersion;

    /// <summary>
    /// Slot that may have a held connection. Separate from <see cref="SelectedSlot"/> because by the time
    /// <see cref="OnSelectedSlotChanged"/> runs, that property already holds the new value.
    /// </summary>
    private SlotProfile? _slotWithHeldSession;

    public ObservableCollection<SlotProfile> Slots => _group.Slots;

    [ObservableProperty]
    private SlotProfile? _selectedSlot;

    [ObservableProperty]
    private HintTargetMode _mode = HintTargetMode.Item;

    /// <summary>Item mode only, off by default: a game can place several copies of the same named item.</summary>
    [ObservableProperty]
    private bool _excludeAlreadyFoundOrHinted;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Kept so toggling <see cref="ExcludeAlreadyFoundOrHinted"/> re-filters without a new fetch.</summary>
    private IReadOnlyList<string> _lastFetchedItemNames = Array.Empty<string>();

    public ObservableCollection<HintPickerRow> FilteredRows { get; } = new();

    public bool IsItemMode
    {
        get => Mode == HintTargetMode.Item;
        set { if (value) Mode = HintTargetMode.Item; }
    }

    public bool IsLocationMode
    {
        get => Mode == HintTargetMode.Location;
        set { if (value) Mode = HintTargetMode.Location; }
    }

    public bool ShowExcludeCheckbox => Mode == HintTargetMode.Item;

    public bool HasNoRowsAtAll => !IsLoading && _availableRows.Count == 0;

    public bool HasNoSearchMatches => !IsLoading && !HasNoRowsAtAll && FilteredRows.Count == 0;

    public string LoadingText => Mode == HintTargetMode.Item ? "Loading items..." : "Loading locations...";

    public HintPickerViewModel(GroupViewModel group, IConnectionManager connectionManager)
    {
        _group = group;
        _connectionManager = connectionManager;
        _selectedSlot = ResolveDefaultSlot();
    }

    private SlotProfile? ResolveDefaultSlot() =>
        (_group.LeaderSlotId is { } leaderId ? _group.Group.Slots.FirstOrDefault(s => s.Id == leaderId) : null)
        ?? _group.Group.Slots.FirstOrDefault();

    /// <summary>
    /// Re-resolves the default slot (the leader may have changed) and reloads. Refreshes explicitly only when the
    /// slot is unchanged - otherwise <see cref="OnSelectedSlotChanged"/> already does, and a second call double-fetches.
    /// </summary>
    public void OnOpened()
    {
        var newSlot = ResolveDefaultSlot();
        var slotUnchanged = Equals(SelectedSlot, newSlot);
        SelectedSlot = newSlot;

        if (slotUnchanged)
        {
            _ = RefreshAvailableRowsAsync();
        }
    }

    partial void OnSelectedSlotChanged(SlotProfile? value)
    {
        var previousSlot = _slotWithHeldSession;
        _slotWithHeldSession = value;
        if (previousSlot is not null)
        {
            _ = _connectionManager.ReleaseHeldSessionAsync(_group, previousSlot);
        }

        _ = RefreshAvailableRowsAsync();
    }

    public Task OnClosedAsync() =>
        _slotWithHeldSession is { } slot ? _connectionManager.ReleaseHeldSessionAsync(_group, slot) : Task.CompletedTask;

    partial void OnModeChanged(HintTargetMode value)
    {
        OnPropertyChanged(nameof(IsItemMode));
        OnPropertyChanged(nameof(IsLocationMode));
        OnPropertyChanged(nameof(ShowExcludeCheckbox));
        OnPropertyChanged(nameof(LoadingText));
        _ = RefreshAvailableRowsAsync();
    }

    partial void OnExcludeAlreadyFoundOrHintedChanged(bool value)
    {
        if (Mode == HintTargetMode.Item && SelectedSlot is { } slot)
        {
            BuildItemRows(slot, _lastFetchedItemNames);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    private async Task RefreshAvailableRowsAsync()
    {
        var slot = SelectedSlot;
        if (slot is null)
        {
            _availableRows = new List<HintPickerRow>();
            OnPropertyChanged(nameof(HasNoRowsAtAll));
            ApplySearchFilter();
            return;
        }

        var version = ++_requestVersion;
        var mode = Mode;
        IsLoading = true;
        OnPropertyChanged(nameof(HasNoRowsAtAll));

        if (mode == HintTargetMode.Item)
        {
            IReadOnlyList<string> itemNames;
            try
            {
                itemNames = await _connectionManager.GetHintableItemsAsync(_group, slot);
            }
            finally
            {
                IsLoading = false;
            }

            if (version != _requestVersion || SelectedSlot != slot || Mode != HintTargetMode.Item)
            {
                return;
            }

            _lastFetchedItemNames = itemNames;
            BuildItemRows(slot, itemNames);
            return;
        }

        IReadOnlyList<HintableLocation> locations;
        try
        {
            locations = await _connectionManager.GetHintableLocationsAsync(_group, slot);
        }
        finally
        {
            IsLoading = false;
        }

        if (version != _requestVersion || SelectedSlot != slot || Mode != HintTargetMode.Location)
        {
            return;
        }

        // GetHintableLocationsAsync excludes checked locations but knows nothing about hints.
        var alreadyHintedLocationNames = new HashSet<string>(
            _group.Hints.Where(h => h.SlotId == slot.Id).Select(h => h.LocationName),
            StringComparer.OrdinalIgnoreCase);

        _availableRows = locations
            .Where(l => !alreadyHintedLocationNames.Contains(l.Name))
            .Select(l => new HintPickerRow { Id = l.LocationId, Name = l.Name })
            .ToList();

        OnPropertyChanged(nameof(HasNoRowsAtAll));
        ApplySearchFilter();
    }

    /// <summary>
    /// The game's DataPackage item names unioned with anything already received/hinted. The exclude filter hides a
    /// name only if received AND without an unfound hint - a heuristic, since the client can't know copy counts.
    /// </summary>
    private void BuildItemRows(SlotProfile slot, IReadOnlyList<string> gameItemNames)
    {
        var receivedNames = new HashSet<string>(
            _group.ReceivedItems.Where(i => i.SlotId == slot.Id).Select(i => i.ItemName),
            StringComparer.OrdinalIgnoreCase);

        // Not HintEntry.SlotId: it can also mean "finder", i.e. another player's item hidden in this world,
        // which would wrongly list items from a different game. Only hints this slot receives belong here.
        var slotHints = _group.Hints.Where(h => IsReceivingPlayer(h, slot)).ToList();
        var hasUnfoundHintByName = slotHints
            .GroupBy(h => h.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(h => !h.Found), StringComparer.OrdinalIgnoreCase);

        var allNames = new HashSet<string>(gameItemNames, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(receivedNames);
        foreach (var hint in slotHints)
        {
            allNames.Add(hint.ItemName);
        }

        IEnumerable<string> names = allNames;
        if (ExcludeAlreadyFoundOrHinted)
        {
            names = names.Where(name =>
            {
                var hasReceived = receivedNames.Contains(name);
                var hasUnfoundHint = hasUnfoundHintByName.TryGetValue(name, out var unfound) && unfound;
                return !(hasReceived && !hasUnfoundHint);
            });
        }

        _availableRows = names
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => new HintPickerRow { Name = n })
            .ToList();

        OnPropertyChanged(nameof(HasNoRowsAtAll));
        ApplySearchFilter();
    }

    /// <summary>
    /// SlotId names the receiver whenever the receiver is a configured slot, so a configured receiver
    /// on this SlotId can only be this slot. Not by name: the display name carries the alias.
    /// </summary>
    private static bool IsReceivingPlayer(HintEntry hint, SlotProfile slot) =>
        hint.SlotId == slot.Id &&
        hint.ReceivingPlayerKind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName;

    private void ApplySearchFilter()
    {
        var query = SearchText.Trim();
        FilteredRows.Clear();

        foreach (var row in _availableRows)
        {
            if (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasNoSearchMatches));
    }

    /// <summary>
    /// Deliberately doesn't close the picker. Location rows are removed (a location exists once); item rows stay,
    /// since a name may have other unhinted copies.
    /// </summary>
    [RelayCommand]
    private async Task SendAsync(HintPickerRow row)
    {
        var slot = SelectedSlot;
        if (slot is null)
        {
            return;
        }

        if (Mode == HintTargetMode.Location)
        {
            await _connectionManager.SendHintAsync(_group, slot, row.Id);
            _availableRows = _availableRows.Where(r => r.Id != row.Id).ToList();
            OnPropertyChanged(nameof(HasNoRowsAtAll));
            ApplySearchFilter();
        }
        else
        {
            await _connectionManager.SendItemHintAsync(_group, slot, row.Name);
        }
    }
}
