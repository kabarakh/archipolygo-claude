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
/// Backs the "Hint..." Flyout in the message row (see MainWindow.axaml) -
/// one per <see cref="GroupViewModel"/>, replacing blind
/// <c>!hint &lt;itemname&gt;</c>/<c>!hint_location &lt;locationname&gt;</c>
/// chat typing with a searchable list. See
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md for the full design history -
/// this class is the real implementation of what
/// TestHarness/HintInputPrototype/HintInputPrototypeViewModel prototyped.
///
/// <see cref="Mode"/>: Location mode goes through
/// <see cref="IConnectionManager.GetHintableLocationsAsync"/>/
/// <see cref="IConnectionManager.SendHintAsync"/> (the structured
/// <c>CreateHints</c> API - needs a real connection). Item mode's candidate
/// pool used to be limited to item names this slot had already encountered
/// somehow (received, or already hinted) - there's no client-side way to
/// resolve an item name to a location id without already knowing where it
/// is, so sending still falls back to the plain <c>!hint &lt;name&gt;</c>
/// chat command via <see cref="IConnectionManager.SendItemHintAsync"/> - but
/// that made the "exclude already found/hinted" checkbox pointless (the
/// whole pool was already nothing but found/hinted names). See
/// Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section: Item mode
/// now also needs a connection, via <see cref="IConnectionManager.GetHintableItemsAsync"/>,
/// which returns every item name the slot's own game defines (the server's
/// DataPackage) - <see cref="BuildItemRows"/> then cross-references that
/// against <see cref="GroupViewModel.ReceivedItems"/>/<see cref="GroupViewModel.Hints"/>
/// for the exclude checkbox, same as before.
///
/// <see cref="SelectedSlot"/> lets the user browse (and, for Location mode,
/// send hints for) any configured slot in the group, not just the current
/// leader - <see cref="IConnectionManager"/> briefly connects as a
/// non-leader slot behind the scenes when needed, without ever touching the
/// group's actual leader connection. Per Feature-Plaene/Archiv/Hint-Eingabefeld.md's
/// "Status" section, such a connection is kept alive (not torn down after
/// each call) for as long as this slot stays selected, so switching Item
/// &lt;-&gt; Location mode or sending several hints in a row for the same
/// slot never reconnects - <see cref="_slotWithHeldSession"/> tracks which
/// slot that connection (if any) belongs to, so it can be released
/// (<see cref="IConnectionManager.ReleaseHeldSessionAsync"/>) the moment the
/// picker moves on: a different slot gets selected, or the window closes
/// (see <see cref="OnClosedAsync"/>).
/// </summary>
public partial class HintPickerViewModel : ObservableObject
{
    private readonly GroupViewModel _group;
    private readonly IConnectionManager _connectionManager;

    /// <summary>The selected slot+mode(+exclusion)'s rows, before the search filter.</summary>
    private List<HintPickerRow> _availableRows = new();

    /// <summary>
    /// Bumped on every <see cref="RefreshAvailableRowsAsync"/> call so a
    /// slower, earlier Location-mode fetch that's still in flight when the
    /// slot/mode changes again can tell it's stale and discard its result
    /// instead of overwriting a newer selection's rows.
    /// </summary>
    private int _requestVersion;

    /// <summary>
    /// Which slot (if any) might currently have a connection held open on
    /// its behalf (see the class doc comment) - always mirrors whatever
    /// <see cref="SelectedSlot"/> was the last time it changed, so
    /// <see cref="OnSelectedSlotChanged"/> knows which slot to release when
    /// the selection moves on to a different one. Deliberately a separate
    /// field rather than reading <c>SelectedSlot</c> itself for this: by the
    /// time that partial method runs, the property already holds the *new*
    /// value.
    /// </summary>
    private SlotProfile? _slotWithHeldSession;

    /// <summary>The group's own already-ordered (leader first, then alphabetical) slot list - reused directly rather than duplicated here, so it stays live as slots are added/removed.</summary>
    public ObservableCollection<SlotProfile> Slots => _group.Slots;

    [ObservableProperty]
    private SlotProfile? _selectedSlot;

    [ObservableProperty]
    private HintTargetMode _mode = HintTargetMode.Item;

    /// <summary>
    /// Item mode only: a game can place several copies of the same named
    /// item, so "already found/hinted" isn't the default exclusion the way
    /// it is for locations (see <see cref="ShowExcludeCheckbox"/>).
    /// </summary>
    [ObservableProperty]
    private bool _excludeAlreadyFoundOrHinted;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>True while a fetch for the current slot+mode is in flight - <see cref="IConnectionManager.GetHintableLocationsAsync"/> for Location mode, <see cref="IConnectionManager.GetHintableItemsAsync"/> for Item mode.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Item mode's most recently fetched raw game item list (before the
    /// exclude-checkbox filter/dedup) - kept around purely so toggling
    /// <see cref="ExcludeAlreadyFoundOrHinted"/> can re-filter instantly
    /// without a whole new fetch, since that list doesn't change just because
    /// the checkbox did.
    /// </summary>
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

    /// <summary>True only in Item mode - see ExcludeAlreadyFoundOrHinted.</summary>
    public bool ShowExcludeCheckbox => Mode == HintTargetMode.Item;

    /// <summary>True once nothing is available for the selected slot+mode(+exclusion) at all, and no fetch is still in flight.</summary>
    public bool HasNoRowsAtAll => !IsLoading && _availableRows.Count == 0;

    /// <summary>True when rows exist for this slot+mode, but none match the current search text.</summary>
    public bool HasNoSearchMatches => !IsLoading && !HasNoRowsAtAll && FilteredRows.Count == 0;

    /// <summary>
    /// Mode-specific text for <see cref="IsLoading"/> - Item mode only
    /// started needing a connection (and therefore a loading state at all)
    /// once its candidate pool became the game's DataPackage item list (see
    /// Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section); this
    /// used to just always say "Loading locations..." since Location mode was
    /// the only one that ever needed it.
    /// </summary>
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
    /// Called when the "Hint..." Flyout opens (see MainWindow.axaml.cs) -
    /// re-resolves the default slot (the leader may have changed since this
    /// was last open) and (re)loads its rows.
    ///
    /// Bug fixed here: assigning <see cref="SelectedSlot"/> already triggers
    /// <see cref="OnSelectedSlotChanged"/> (which itself calls
    /// <see cref="RefreshAvailableRowsAsync"/>) whenever the resolved slot
    /// actually differs from before - which used to be *every* first open of
    /// a session, since the constructor runs before this group has any
    /// configured slots yet, leaving <see cref="SelectedSlot"/> null until
    /// this method's first real call. Unconditionally calling
    /// <see cref="RefreshAvailableRowsAsync"/> again right after meant a
    /// silent double-fetch on every such open - for Item mode, one wasted
    /// DataPackage request; for Location mode, a whole second probe-connect
    /// for a non-leader slot. Only fire it explicitly when the slot did NOT
    /// change (the common case reopening the picker without switching
    /// leaders), since then the setter had nothing to notify about.
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
        // Release whatever slot was selected before this one - if a
        // connection was ever held for it (browse or send), it has no
        // reason to stay open now that the picker has moved on. Null only on
        // the very first-ever change (nothing to release yet).
        var previousSlot = _slotWithHeldSession;
        _slotWithHeldSession = value;
        if (previousSlot is not null)
        {
            _ = _connectionManager.ReleaseHeldSessionAsync(_group, previousSlot);
        }

        _ = RefreshAvailableRowsAsync();
    }

    /// <summary>
    /// Called when the "Hint..." picker window actually closes (see
    /// <see cref="Views.HintPickerWindow.Show"/>) - releases whatever slot's
    /// connection might currently be held (see the class doc comment), so
    /// closing the window (rather than switching to a different slot first,
    /// which already releases the old one via <see cref="OnSelectedSlotChanged"/>)
    /// doesn't leave it lingering open indefinitely.
    /// </summary>
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
            // Re-filter the already-fetched list - no need to re-fetch just
            // because the checkbox changed, see _lastFetchedItemNames.
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

            // Stale if the slot/mode moved on while this was in flight - a
            // newer call already owns _availableRows now.
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

        // GetHintableLocationsAsync already excludes checked locations (via
        // AllMissingLocations) but knows nothing about hints - that
        // cross-reference happens here, against the UI-owned Hints list.
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
    /// Item mode's whole candidate pool: every item name <paramref name="gameItemNames"/>
    /// says the slot's own game defines (see <see cref="IConnectionManager.GetHintableItemsAsync"/>
    /// and Feature-Plaene/Archiv/Hint-Eingabefeld.md's "Status" section),
    /// unioned with anything already received/hinted (see below) in case a
    /// name somehow isn't in that table. <see cref="ExcludeAlreadyFoundOrHinted"/>,
    /// when on, hides a name only if it's been received AND has no
    /// currently-unfound hint - a best-effort heuristic (the exact total copy
    /// count of a named item is not something the client can know from just
    /// the DataPackage's name list), not a mathematical guarantee every copy
    /// is accounted for.
    /// </summary>
    private void BuildItemRows(SlotProfile slot, IReadOnlyList<string> gameItemNames)
    {
        var receivedNames = new HashSet<string>(
            _group.ReceivedItems.Where(i => i.SlotId == slot.Id).Select(i => i.ItemName),
            StringComparer.OrdinalIgnoreCase);

        // HintEntry.SlotId alone is NOT enough here - it's set to whichever
        // configured slot the hint concerns *either* as receiver or, only if
        // the actual receiver isn't a configured slot at all, as finder
        // (see ConnectionManager's hint-snapshot building). A hint where
        // `slot` merely happens to be the finder is some OTHER player's item
        // physically hidden in `slot`'s own world - showing that item under
        // `slot`'s own hintable-items list would be wrong (dev-reported bug:
        // a Kirby Super Star slot's item list showed items like "Memory of a
        // Distant World" that only exist in a different, linked game's own
        // pool). Only a hint where `slot` is actually the *receiving* player
        // belongs here - checked by name/alias, same convention used
        // everywhere else in this codebase for matching a player to a
        // configured slot.
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
    /// True if <paramref name="slot"/> is genuinely the receiving player of
    /// <paramref name="hint"/> - by slot name or (if set) room alias, the
    /// same two names <c>HintEntry.ReceivingPlayerName</c> can actually come
    /// back as (see <see cref="Models.HintEntry"/> and
    /// <c>ConnectionManager</c>'s hint-snapshot building, which resolves it
    /// via <c>session.Players.GetPlayerAlias</c>). See
    /// <see cref="BuildItemRows"/> for why this - not <c>HintEntry.SlotId</c>
    /// - is the correct check for "is this my own item".
    /// </summary>
    private static bool IsReceivingPlayer(HintEntry hint, SlotProfile slot) =>
        string.Equals(hint.ReceivingPlayerName, slot.SlotName, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrEmpty(slot.Alias) && string.Equals(hint.ReceivingPlayerName, slot.Alias, StringComparison.OrdinalIgnoreCase));

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
    /// Sends the hint for one row and, per dev feedback while prototyping
    /// this (see Feature-Plaene/Archiv/Hint-Eingabefeld.md), does NOT close
    /// the Flyout - several hints can be sent in one sitting. Location mode
    /// removes the row immediately (unambiguous - a location only exists
    /// once); Item mode leaves it in place, since a deduped name might still
    /// have other unhinted copies this app has no way to tell apart from the
    /// one just hinted.
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
