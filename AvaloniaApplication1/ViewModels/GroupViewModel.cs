using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>
/// Represents a tab in the MainWindow, i.e. one Archipelago server. Since
/// Phase 6, a server can have several configured slots but only ever one
/// live connection (the "leader", see <see cref="LeaderSlotId"/>); this view
/// model holds one merged event log, hint list and received-items list for
/// every slot configured on the server, each entry tagged with which slot it
/// is about (see <see cref="EventEntry.SlotId"/> and friends) so the slot
/// filter dropdown can narrow the view down to one slot. Actual connection
/// handling is delegated to <see cref="IConnectionManager"/>.
/// </summary>
public partial class GroupViewModel : ViewModelBase
{
    private readonly IConnectionManager _connectionManager;

    /// <summary>
    /// Optional - null in every existing test construction site that doesn't
    /// care about Tier 2 (Feature-Plaene/Archiv/Fortschrittsanzeigen.md's whole-
    /// multiworld progress), so this stays a purely additive dependency
    /// rather than forcing every other call site to thread one through. Null
    /// just means <see cref="RefreshMultiworldProgressAsync"/> silently no-ops.
    /// </summary>
    private readonly IMultiworldTrackerService? _multiworldTrackerService;

    /// <summary>Guards <see cref="OnSelectedChatSlotChanged"/> while a switch/initial-select is already applying, so it doesn't re-enter itself.</summary>
    private bool _applyingLeaderChange;

    [ObservableProperty]
    private ServerConnectionGroup _group;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    /// <summary>
    /// Id of the <see cref="SlotProfile"/> that currently holds the one live,
    /// persistent connection for this server ("leader"), or null if none.
    /// </summary>
    [ObservableProperty]
    private Guid? _leaderSlotId;

    /// <summary>
    /// Bound to the account dropdown. Selecting a different slot here is the
    /// one action that triggers a disconnect/reconnect (see
    /// <see cref="OnSelectedChatSlotChanged"/>). Kept separate from
    /// <see cref="LeaderSlotId"/> so the dropdown can reflect "switch in
    /// progress" without flapping back and forth while the handshake is
    /// still running. The dropdown's ItemsSource (<see cref="Slots"/>) never
    /// contains a null entry, so the user can never actually select null
    /// here - null only ever arrives programmatically, from
    /// <see cref="SetLeaderStateWithoutTriggeringSwitch"/> after a real
    /// disconnect (guarded, see <see cref="OnSelectedChatSlotChanged"/>) or
    /// as a spurious rebinding artifact, which is ignored rather than
    /// treated as a disconnect request - see the Disconnect button/command
    /// for that instead.
    /// </summary>
    [ObservableProperty]
    private SlotProfile? _selectedChatSlot;

    /// <summary>Narrows <see cref="VisibleEvents"/> down to one configured slot; null = show all slots mixed together (the default). Independent of the Hints/Items slot filters below.</summary>
    [ObservableProperty]
    private SlotProfile? _selectedEventsSlotFilter;

    /// <summary>Narrows <see cref="VisibleHints"/> down to one configured slot; null = show all slots mixed together (the default). Independent of the Events/Items slot filters.</summary>
    [ObservableProperty]
    private SlotProfile? _selectedHintsSlotFilter;

    /// <summary>Narrows <see cref="VisibleReceivedItems"/> down to one configured slot; null = show all slots mixed together (the default). Independent of the Events/Hints slot filters.</summary>
    [ObservableProperty]
    private SlotProfile? _selectedItemsSlotFilter;

    [ObservableProperty]
    private int _unreadEventCount;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private HintFilter _selectedHintFilter = HintFilter.Unfound;

    [ObservableProperty]
    private HintRoleFilter _selectedHintRoleFilter = HintRoleFilter.All;

    /// <summary>
    /// Narrows <see cref="VisibleHints"/> down by item category, using the
    /// same <see cref="ItemCategoryFilter"/> enum and single-select
    /// filter-toggle-button pattern as <see cref="SelectedItemCategoryFilter"/>
    /// on the received-items list - but a separate property, so the two
    /// lists' category filters don't affect each other (see also the
    /// Progression/Useful/Filler/Trap checkboxes on the Events list, which
    /// are a third, independent filter dimension of their own).
    /// </summary>
    [ObservableProperty]
    private ItemCategoryFilter _selectedHintItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private EventRelevanceFilter _selectedEventRelevanceFilter = EventRelevanceFilter.All;

    [ObservableProperty]
    private EventCategoryFilter _selectedEventCategoryFilter = EventCategoryFilter.All;

    /// <summary>
    /// Independent checkbox filter narrowing <see cref="VisibleEvents"/> by
    /// item category (see <see cref="EventEntry.ItemKind"/>), combining with
    /// <see cref="SelectedEventCategoryFilter"/> rather than replacing it -
    /// e.g. "Items" + only "Trap" checked shows just trap items; since hint
    /// entries also carry an item classification, unchecking a category
    /// hides matching hints too. Entries with no item category at all
    /// (connect/disconnect/chat/error) are unaffected by these four and
    /// always pass through. Display-only, like every other event filter
    /// here - the underlying <see cref="Events"/> collection this reads
    /// from is never touched.
    /// </summary>
    [ObservableProperty]
    private bool _showProgressionItemEvents = true;

    [ObservableProperty]
    private bool _showUsefulItemEvents = true;

    [ObservableProperty]
    private bool _showFillerItemEvents = true;

    [ObservableProperty]
    private bool _showTrapItemEvents = true;

    [ObservableProperty]
    private RightPanelView _selectedRightPanel = RightPanelView.Hints;

    [ObservableProperty]
    private ItemCategoryFilter _selectedItemCategoryFilter = ItemCategoryFilter.All;

    [ObservableProperty]
    private string _itemSearchText = string.Empty;

    [ObservableProperty]
    private string _messageToSend = string.Empty;

    // Shell/chat-style history for the message box's Up/Down-arrow recall
    // (OnMessageTextBoxKeyDown in MainWindow.axaml.cs) - most useful for
    // resending the same !hint text for an item several times in a row.
    // _messageHistoryIndex == -1 means "not currently navigating history";
    // while navigating it points at the entry currently shown in the box,
    // and _messageHistoryDraft holds whatever the user had typed before the
    // first Up-press so Down can restore it once they've stepped back past
    // the newest entry - exactly like a terminal's command history.
    private const int MaxMessageHistory = 50;
    private readonly List<string> _messageHistory = new();
    private int _messageHistoryIndex = -1;
    private string? _messageHistoryDraft;

    public ObservableCollection<EventEntry> Events { get; } = new();

    public ObservableCollection<HintEntry> Hints { get; } = new();

    public ObservableCollection<ReceivedItemEntry> ReceivedItems { get; } = new();

    /// <summary>
    /// Every configured slot on this server, for the "Chat as" dropdown -
    /// ordered with the current leader (see <see cref="LeaderSlotId"/>)
    /// first and every other slot alphabetically by name, rather than raw
    /// <see cref="Group"/> insertion order (see <see cref="RefreshSlotOrder"/>).
    /// A separately maintained collection rather than a passthrough to
    /// <c>Group.Slots</c>, since it needs its own order independent of that
    /// collection's.
    /// </summary>
    public ObservableCollection<SlotProfile> Slots { get; } = new();

    public bool IsLeaderConnected => LeaderSlotId is not null && ConnectionState == ConnectionState.Connected;

    public bool CanDisconnect => LeaderSlotId is not null || ConnectionState is ConnectionState.Connecting or ConnectionState.Reconnecting;

    /// <summary>
    /// Mirror of <see cref="CanDisconnect"/> for the Connect button that
    /// replaces Disconnect while the group is offline - picking a leader via
    /// the "Chat as" dropdown to get connected wasn't discoverable, so this
    /// gives the not-connected state an explicit affirmative action instead.
    /// Requires a configured slot to connect as (nothing to pick otherwise).
    /// </summary>
    public bool CanConnect => !CanDisconnect && Slots.Count > 0;

    /// <summary>
    /// Hints filtered by found/unfound, role ("mine" = concerns any configured
    /// slot, i.e. <see cref="EventTextSegmentKind.OwnSlotName"/> or
    /// <see cref="EventTextSegmentKind.ConnectedSlotName"/>), the Hints slot
    /// filter dropdown, and the search box. The dimensions combine independently.
    /// </summary>
    public IEnumerable<HintEntry> VisibleHints
    {
        get
        {
            IEnumerable<HintEntry> hints = Hints;

            if (SelectedHintFilter == HintFilter.Unfound)
                hints = hints.Where(h => !h.Found);

            hints = SelectedHintRoleFilter switch
            {
                HintRoleFilter.IFind    => hints.Where(h => IsMine(h.FindingPlayerKind)),
                HintRoleFilter.IReceive => hints.Where(h => IsMine(h.ReceivingPlayerKind)),
                _                       => hints,
            };

            hints = SelectedHintItemCategoryFilter switch
            {
                ItemCategoryFilter.Progress => hints.Where(h => h.ItemKind == EventTextSegmentKind.ItemProgression),
                ItemCategoryFilter.Useful   => hints.Where(h => h.ItemKind == EventTextSegmentKind.ItemUseful),
                ItemCategoryFilter.Normal   => hints.Where(h => h.ItemKind == EventTextSegmentKind.ItemOther),
                ItemCategoryFilter.Trap     => hints.Where(h => h.ItemKind == EventTextSegmentKind.ItemTrap),
                _                           => hints,
            };

            if (SelectedHintsSlotFilter is not null)
                hints = hints.Where(h => h.SlotId == SelectedHintsSlotFilter.Id);

            if (!string.IsNullOrEmpty(ItemSearchText))
                hints = hints.Where(h => h.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));

            return hints;
        }
    }

    private static bool IsMine(EventTextSegmentKind kind) =>
        kind is EventTextSegmentKind.OwnSlotName or EventTextSegmentKind.ConnectedSlotName;

    public int UnfoundHintCount => Hints.Count(h => !h.Found);

    public int UnfoundHintIFindCount => Hints.Count(h => !h.Found && IsMine(h.FindingPlayerKind));

    public int UnfoundHintIReceiveCount => Hints.Count(h => !h.Found && IsMine(h.ReceivingPlayerKind));

    public string HintsPanelButtonText => UnfoundHintCount > 0 ? $"Hints ({UnfoundHintCount})" : "Hints";

    public string HintsIFindButtonText => UnfoundHintIFindCount > 0 ? $"My location ({UnfoundHintIFindCount})" : "My location";

    public string HintsIReceiveButtonText => UnfoundHintIReceiveCount > 0 ? $"My item ({UnfoundHintIReceiveCount})" : "My item";

    /// <summary>
    /// <see cref="Events"/> filtered by relevance, category and the Events
    /// slot filter dropdown. Room-wide chat lines (<see cref="EventEntry.SlotId"/>
    /// is null) always pass the slot filter, since they aren't tied to any
    /// one configured slot in the first place.
    /// </summary>
    public IEnumerable<EventEntry> VisibleEvents
    {
        get
        {
            IEnumerable<EventEntry> events = Events;

            if (SelectedEventRelevanceFilter == EventRelevanceFilter.ConcernsMe)
            {
                events = events.Where(e => e.ConcernsOwnSlot);
            }

            events = SelectedEventCategoryFilter switch
            {
                EventCategoryFilter.Hints => events.Where(e => e.Type == EventType.HintReceived),
                EventCategoryFilter.Items => events.Where(e => e.Type == EventType.ItemReceived),
                EventCategoryFilter.Chat => events.Where(e => e.Type == EventType.Chat),
                _ => events,
            };

            events = events.Where(e => e.ItemKind switch
            {
                EventTextSegmentKind.ItemProgression => ShowProgressionItemEvents,
                EventTextSegmentKind.ItemUseful => ShowUsefulItemEvents,
                EventTextSegmentKind.ItemOther => ShowFillerItemEvents,
                EventTextSegmentKind.ItemTrap => ShowTrapItemEvents,
                _ => true, // not an item-carrying entry - this filter doesn't apply to it
            });

            if (SelectedEventsSlotFilter is not null)
            {
                var filterId = SelectedEventsSlotFilter.Id;
                events = events.Where(e => e.SlotId is null || e.SlotId == filterId);
            }

            return events;
        }
    }

    public IEnumerable<ReceivedItemEntry> VisibleReceivedItems
    {
        get
        {
            IEnumerable<ReceivedItemEntry> items = ReceivedItems;

            items = SelectedItemCategoryFilter switch
            {
                ItemCategoryFilter.Progress => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemProgression),
                ItemCategoryFilter.Useful   => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemUseful),
                ItemCategoryFilter.Normal   => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemOther),
                ItemCategoryFilter.Trap     => items.Where(i => i.ItemKind == EventTextSegmentKind.ItemTrap),
                _                           => items,
            };

            if (SelectedItemsSlotFilter is not null)
                items = items.Where(i => i.SlotId == SelectedItemsSlotFilter.Id);

            if (!string.IsNullOrEmpty(ItemSearchText))
            {
                items = items.Where(i =>
                    i.ItemName.Contains(ItemSearchText, StringComparison.OrdinalIgnoreCase));
            }

            return items;
        }
    }

    public bool HasUnreadEvents => UnreadEventCount > 0;

    public string HeaderText => Group.Name;

    /// <summary>
    /// <see cref="Slots"/> plus a leading null entry representing "All
    /// slots", shared as the <c>ItemsSource</c> for all three independent
    /// slot filter dropdowns (<see cref="SelectedEventsSlotFilter"/>,
    /// <see cref="SelectedHintsSlotFilter"/>, <see cref="SelectedItemsSlotFilter"/>
    /// - each binds its own <c>SelectedItem</c> to this same list); a
    /// <see cref="Converters.SlotFilterDisplayConverter"/> turns the null
    /// entry into an "All slots" label. The leading null aside, ordered the
    /// same way as <see cref="Slots"/> - leader first, then alphabetical -
    /// kept in sync by the same <see cref="RefreshSlotOrder"/> rebuild
    /// rather than recomputed on every access, so the dropdowns update live
    /// when a slot is added/removed or the leader changes.
    /// </summary>
    public ObservableCollection<SlotProfile?> SlotFilterOptions { get; } = new() { null };

    /// <summary>
    /// Backs the "Hint..." Flyout in the message row (see MainWindow.axaml) -
    /// see <see cref="HintPickerViewModel"/>'s own doc comment for the full
    /// design. One instance per group, created alongside it rather than
    /// lazily on first open, so its own state (selected slot/mode/search
    /// text) persists across opens within the same session.
    /// </summary>
    public HintPickerViewModel HintPicker { get; }

    public GroupViewModel(ServerConnectionGroup group, IConnectionManager connectionManager, IMultiworldTrackerService? multiworldTrackerService = null)
    {
        _group = group;
        _connectionManager = connectionManager;
        _multiworldTrackerService = multiworldTrackerService;
        HintPicker = new HintPickerViewModel(this, connectionManager);

        Events.CollectionChanged += OnEventsCollectionChanged;
        Hints.CollectionChanged += OnHintsCollectionChanged;
        ReceivedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(VisibleReceivedItems));
        Group.PropertyChanged += OnGroupPropertyChanged;

        RefreshSlotOrder();

        foreach (var slot in Group.Slots)
        {
            SubscribeSlotProgress(slot);
        }

        Group.Slots.CollectionChanged += OnSlotsCollectionChanged;
    }

    private void OnSlotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (SlotProfile slot in e.OldItems)
            {
                UnsubscribeSlotProgress(slot);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (SlotProfile slot in e.NewItems)
            {
                SubscribeSlotProgress(slot);
            }
        }

        RefreshSlotOrder();
        RaiseRoomProgressChanged();
    }

    private void SubscribeSlotProgress(SlotProfile slot) => slot.PropertyChanged += OnSlotProgressPropertyChanged;

    private void UnsubscribeSlotProgress(SlotProfile slot) => slot.PropertyChanged -= OnSlotProgressPropertyChanged;

    private void OnSlotProgressPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlotProfile.LocationsChecked) or nameof(SlotProfile.LocationsTotal))
        {
            RaiseRoomProgressChanged();
        }
    }

    /// <summary>
    /// Tier 1 of Fortschrittsanzeigen.md: how many of this room's own
    /// configured slots' locations have been checked so far, summed over
    /// every slot that has ever successfully synced (see
    /// <see cref="SlotProfile.LocationsChecked"/>) - a slot that has never
    /// connected contributes nothing rather than a misleading 0.
    /// </summary>
    public int RoomChecksCompleted => Group.Slots.Where(s => s.LocationsChecked is not null).Sum(s => s.LocationsChecked!.Value);

    public int RoomChecksTotal => Group.Slots.Where(s => s.LocationsTotal is not null).Sum(s => s.LocationsTotal!.Value);

    /// <summary>Whether at least one configured slot has ever synced its location progress - drives whether the aggregated progress bar is shown at all.</summary>
    public bool HasRoomProgress => Group.Slots.Any(s => s.LocationsTotal is not null);

    public string RoomProgressText => HasRoomProgress ? $"{RoomChecksCompleted}/{RoomChecksTotal}" : string.Empty;

    /// <summary>0-100. Exposed under this exact name for Feature-Plaene/Dashboard-Tab.md, which plans to show this as an app-wide aggregate once that dashboard exists - see that plan's "Sobald der Fortschrittsanzeigen-Plan umgesetzt ist" note.</summary>
    public double RoomProgressPercent => RoomChecksTotal > 0 ? (double)RoomChecksCompleted / RoomChecksTotal * 100.0 : 0;

    private void RaiseRoomProgressChanged()
    {
        OnPropertyChanged(nameof(RoomChecksCompleted));
        OnPropertyChanged(nameof(RoomChecksTotal));
        OnPropertyChanged(nameof(HasRoomProgress));
        OnPropertyChanged(nameof(RoomProgressText));
        OnPropertyChanged(nameof(RoomProgressPercent));

        // OwnChecksDone/Total (part of the combined Tier 1+2 bar) are Tier 1
        // values themselves - see RaiseCombinedProgressChanged's doc comment.
        RaiseCombinedProgressChanged();
    }

    /// <summary>Tier 2 of Fortschrittsanzeigen.md: whether this group has a resolved tracker id to poll at all - drives whether the whole-multiworld progress section shows up.</summary>
    public bool HasMultiworldTracker => !string.IsNullOrWhiteSpace(Group.TrackerId);

    [ObservableProperty]
    private bool _isRefreshingMultiworldProgress;

    /// <summary>Set on a failed refresh (network error, tracker not found, ...) - see <see cref="IMultiworldTrackerService"/>'s "never throws" contract. Cleared on the next successful refresh.</summary>
    [ObservableProperty]
    private string? _multiworldProgressError;

    /// <summary>
    /// Every player's whole-multiworld progress, from the room's webhost
    /// tracker - see <see cref="RefreshMultiworldProgressAsync"/>. Empty
    /// until the first successful refresh. Not bound to directly by the UI
    /// (a per-player list/bar would mean one bar per player in the room -
    /// hundreds for a large multiworld) - only ever aggregated, see
    /// <see cref="OwnChecksDone"/>/<see cref="OtherChecksDone"/> and friends
    /// below.
    /// </summary>
    public ObservableCollection<PlayerProgress> MultiworldProgress { get; } = new();

    /// <summary>Sum of every tracked player's <see cref="PlayerProgress.ChecksDone"/> - the whole room, this app's own configured slots included.</summary>
    public int MultiworldChecksDone => MultiworldProgress.Sum(p => p.ChecksDone);

    /// <summary>Sum of every tracked player's <see cref="PlayerProgress.ChecksTotal"/> (treating "not yet known" as 0) - the whole room's total.</summary>
    public int MultiworldChecksTotal => MultiworldProgress.Sum(p => p.ChecksTotal ?? 0);

    /// <summary>
    /// This app's own configured slots' share of the combined Tier 1+2 bar -
    /// deliberately just <see cref="RoomChecksCompleted"/>/<see cref="RoomChecksTotal"/>
    /// (Tier 1's own live-connection data) rather than trying to pick "which
    /// of the tracker's numeric players are mine": the tracker API gives no
    /// reliable slot-name mapping unless the tracker was resolved from a room
    /// URL (see <see cref="Services.IMultiworldTrackerService.ResolveTrackerIdAsync"/>),
    /// and a player's tracker alias need not match its configured slot name
    /// at all. Subtracting this from the tracker's whole-room totals (see
    /// <see cref="OtherChecksDone"/>) sidesteps that matching problem
    /// entirely - two independently-sourced aggregates instead of one
    /// per-player join.
    /// </summary>
    public int OwnChecksDone => RoomChecksCompleted;

    public int OwnChecksTotal => RoomChecksTotal;

    public int OwnChecksOpen => Math.Max(0, OwnChecksTotal - OwnChecksDone);

    /// <summary>
    /// Everyone else in the room: the tracker's whole-room total minus this
    /// app's own slots' Tier 1 total. Clamped to never go negative - Tier 1
    /// (live, updates instantly) and Tier 2 (polled, up to 60s/300s stale,
    /// see <see cref="IMultiworldTrackerService"/>) can briefly disagree
    /// about "own", e.g. right after a check that Tier 1 already reflects
    /// but the tracker hasn't polled again for yet.
    /// </summary>
    public int OtherChecksDone => Math.Max(0, MultiworldChecksDone - OwnChecksDone);

    public int OtherChecksTotal => Math.Max(0, MultiworldChecksTotal - OwnChecksTotal);

    public int OtherChecksOpen => Math.Max(0, OtherChecksTotal - OtherChecksDone);

    /// <summary>
    /// Whether there's anything at all to show in the one combined progress
    /// bar - true the moment at least one own configured slot has synced,
    /// even with no tracker configured at all (see the bar's own doc comment
    /// in MainWindow.axaml: the fallback case is just this same bar with its
    /// two "other" segments collapsed to zero width, not a separate element).
    /// </summary>
    public bool HasAnyProgress => OwnChecksTotal + OtherChecksTotal > 0;

    /// <summary>The four segments' combined denominator - what each segment's own share of the bar (and its legend percentage) is measured against.</summary>
    private int GrandTotalChecks => OwnChecksTotal + OtherChecksTotal;

    private static string FormatPercentOfGrandTotal(int part, int grandTotal) =>
        grandTotal > 0 ? $"{Math.Round(part * 100.0 / grandTotal)}%" : "0%";

    // Legend lines for the combined bar's hover tooltip (see MainWindow.axaml) -
    // the on-bar text label was removed once the tooltip took over showing
    // absolute numbers/percentages, per user request.
    public string OwnChecksDoneLegendText => $"Own, done: {OwnChecksDone} ({FormatPercentOfGrandTotal(OwnChecksDone, GrandTotalChecks)})";

    public string OwnChecksOpenLegendText => $"Own, open: {OwnChecksOpen} ({FormatPercentOfGrandTotal(OwnChecksOpen, GrandTotalChecks)})";

    public string OtherChecksDoneLegendText => $"Others, done: {OtherChecksDone} ({FormatPercentOfGrandTotal(OtherChecksDone, GrandTotalChecks)})";

    public string OtherChecksOpenLegendText => $"Others, open: {OtherChecksOpen} ({FormatPercentOfGrandTotal(OtherChecksOpen, GrandTotalChecks)})";

    private void RaiseCombinedProgressChanged()
    {
        OnPropertyChanged(nameof(MultiworldChecksDone));
        OnPropertyChanged(nameof(MultiworldChecksTotal));
        OnPropertyChanged(nameof(OwnChecksDone));
        OnPropertyChanged(nameof(OwnChecksTotal));
        OnPropertyChanged(nameof(OwnChecksOpen));
        OnPropertyChanged(nameof(OtherChecksDone));
        OnPropertyChanged(nameof(OtherChecksTotal));
        OnPropertyChanged(nameof(OtherChecksOpen));
        OnPropertyChanged(nameof(HasAnyProgress));
        OnPropertyChanged(nameof(OwnChecksDoneLegendText));
        OnPropertyChanged(nameof(OwnChecksOpenLegendText));
        OnPropertyChanged(nameof(OtherChecksDoneLegendText));
        OnPropertyChanged(nameof(OtherChecksOpenLegendText));
    }

    /// <summary>
    /// Fetches (or re-fetches) Tier 2 progress for this room - see
    /// <see cref="IMultiworldTrackerService.GetProgressAsync"/>, which itself
    /// throttles to the tracker API's own documented cache timers regardless
    /// of how often this is called. No-op if this group has no resolved
    /// tracker id, or no <see cref="IMultiworldTrackerService"/> was supplied
    /// at all (every existing construction site that doesn't need Tier 2).
    /// </summary>
    [RelayCommand]
    private async Task RefreshMultiworldProgressAsync()
    {
        if (_multiworldTrackerService is null || string.IsNullOrWhiteSpace(Group.TrackerId))
        {
            return;
        }

        IsRefreshingMultiworldProgress = true;
        try
        {
            var snapshot = await _multiworldTrackerService.GetProgressAsync(Group.TrackerId);
            if (snapshot is null)
            {
                MultiworldProgressError = "Could not fetch multiworld progress right now.";
                return;
            }

            MultiworldProgressError = null;
            MultiworldProgress.Clear();
            foreach (var player in snapshot.Players.OrderBy(p => p.Player))
            {
                MultiworldProgress.Add(player);
            }

            RaiseCombinedProgressChanged();
        }
        finally
        {
            IsRefreshingMultiworldProgress = false;
        }
    }

    /// <summary>
    /// Adds several new slots to <see cref="Group"/>'s <c>Slots</c> in one
    /// go (see <see cref="MainWindowViewModel.AddSlotsToGroup"/>, the "Add
    /// slot" search+multi-select picker's batch confirm) via
    /// <see cref="InsertSlotsInOrder"/> rather than <see cref="RefreshSlotOrder"/>'s
    /// full clear+rebuild - see that method's doc comment for why a Clear
    /// (even just once, for the whole batch) isn't safe here.
    /// </summary>
    public void AddSlotsToGroup(IEnumerable<SlotProfile> slots)
    {
        var newSlots = slots as IReadOnlyCollection<SlotProfile> ?? slots.ToList();

        Group.Slots.CollectionChanged -= OnSlotsCollectionChanged;
        try
        {
            foreach (var slot in newSlots)
            {
                Group.Slots.Add(slot);
                SubscribeSlotProgress(slot);
            }
        }
        finally
        {
            Group.Slots.CollectionChanged += OnSlotsCollectionChanged;
        }

        InsertSlotsInOrder(newSlots);
        RaiseRoomProgressChanged();
    }

    /// <summary>
    /// Inserts newly-added slots into <see cref="Slots"/>/<see cref="SlotFilterOptions"/>
    /// at their correct sorted position (see <see cref="RefreshSlotOrder"/>
    /// for the sort rule: leader first, then alphabetical) via
    /// <c>Insert</c> rather than <see cref="RefreshSlotOrder"/>'s
    /// Clear+rebuild. A brand-new slot can never already be the leader, so
    /// it only ever needs inserting into the alphabetical tail, never index
    /// 0 - which means <see cref="Slots"/> never needs to go through an
    /// empty intermediate state the way a full Clear does.
    ///
    /// That distinction matters: <see cref="RefreshSlotOrder"/>'s doc
    /// comment calls a transient-empty-then-repopulated <see cref="Slots"/>
    /// "harmless" for the "Chat as" ComboBox, relying on the "spurious null"
    /// guard in <see cref="OnSelectedChatSlotChanged"/> to restore
    /// <see cref="SelectedChatSlot"/> afterward. In practice, while
    /// <see cref="Slots"/> is briefly empty, Avalonia's ComboBox can push a
    /// second, *nested* null back through the two-way binding as soon as the
    /// guard's own restoring write happens (since the value it's restoring
    /// to still isn't in the empty ItemsSource yet) - and because that
    /// nested write lands while the guard flag is still set, the guard
    /// itself swallows it without re-restoring, permanently dropping
    /// <see cref="SelectedChatSlot"/> to null instead of self-healing. This
    /// is what actually caused the leader to visibly disappear from the
    /// account dropdown right when confirming the "Add slot" dialog.
    /// Inserting instead of clearing sidesteps the whole race, since the
    /// leader's entry never leaves <see cref="Slots"/> in the first place.
    /// </summary>
    private void InsertSlotsInOrder(IEnumerable<SlotProfile> newSlots)
    {
        foreach (var slot in newSlots.OrderBy(s => s.SlotName, StringComparer.OrdinalIgnoreCase))
        {
            // Index 0 is reserved for the leader (if present) regardless of
            // its name - alphabetical ordering only governs the rest, so
            // start scanning right after it instead of comparing against it.
            var index = LeaderSlotId is not null && Slots.Count > 0 && Slots[0].Id == LeaderSlotId ? 1 : 0;

            while (index < Slots.Count &&
                   string.Compare(Slots[index].SlotName, slot.SlotName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }

            Slots.Insert(index, slot);
            SlotFilterOptions.Insert(index + 1, slot); // +1 for SlotFilterOptions' leading "All slots" null entry.
        }
    }

    /// <summary>
    /// Removes one already-configured slot from <see cref="Group"/>'s
    /// <c>Slots</c> (see <see cref="MainWindowViewModel.RemoveSlotFromGroup"/>)
    /// via a targeted <see cref="Slots"/>/<see cref="SlotFilterOptions"/>
    /// removal rather than letting <see cref="OnSlotsCollectionChanged"/>
    /// run its normal <see cref="RefreshSlotOrder"/> - same reasoning as
    /// <see cref="InsertSlotsInOrder"/>'s doc comment (that method's fix for
    /// the equivalent add-side bug): a plain <c>Remove</c> never puts
    /// <see cref="Slots"/> through an empty intermediate state the way
    /// <see cref="RefreshSlotOrder"/>'s <c>Clear</c> does, so the "Chat as"
    /// ComboBox's <c>SelectedItem</c> binding never sees a moment where the
    /// currently-selected (and still valid) slot isn't in the ItemsSource -
    /// which is exactly the race that used to drop <see cref="SelectedChatSlot"/>
    /// to null (the leader silently vanishing from the account dropdown)
    /// whenever a *different* slot on the same server was removed. Removing
    /// the slot that IS currently selected is unaffected by this fix and
    /// stays exactly as safe as before: <see cref="MainWindowViewModel.RemoveSlotFromGroup"/>
    /// disconnects that slot first, which clears <see cref="SelectedChatSlot"/>
    /// itself under <see cref="SetLeaderStateWithoutTriggeringSwitch"/>'s own
    /// re-entrancy guard before this method ever runs.
    /// </summary>
    public void RemoveSlotFromGroup(SlotProfile slot)
    {
        Group.Slots.CollectionChanged -= OnSlotsCollectionChanged;
        try
        {
            Group.Slots.Remove(slot);
            UnsubscribeSlotProgress(slot);
        }
        finally
        {
            Group.Slots.CollectionChanged += OnSlotsCollectionChanged;
        }

        Slots.Remove(slot);
        SlotFilterOptions.Remove(slot);
        RaiseRoomProgressChanged();
    }

    /// <summary>
    /// Rebuilds <see cref="Slots"/> (the "Chat as" dropdown) and, after its
    /// fixed leading "All slots" null entry, <see cref="SlotFilterOptions"/>
    /// from <see cref="Group"/>'s configured slots - sorted so the current
    /// leader (<see cref="LeaderSlotId"/>) always comes first and every
    /// other slot follows alphabetically by name, rather than raw insertion
    /// order, which stops being readable once a server has more than a
    /// handful of slots.
    ///
    /// A full rebuild rather than an incremental add/remove patch, since
    /// this also needs to re-run whenever <see cref="LeaderSlotId"/> itself
    /// changes (see <see cref="OnLeaderSlotIdChanged"/>), not just when a
    /// slot is added or removed (though <see cref="AddSlotsToGroup"/>/
    /// <see cref="RemoveSlotFromGroup"/> now bypass this entirely for their
    /// own cases - see their doc comments) - and with realistically at most
    /// a few dozen slots, re-sorting the whole list each time is cheap.
    ///
    /// Clearing and re-adding items transiently empties <see cref="Slots"/>/
    /// <see cref="SlotFilterOptions"/>, which - without the capture/restore
    /// below - silently drops whichever slot each of the four dropdowns
    /// bound to them (<see cref="SelectedChatSlot"/> and the three
    /// <c>SelectedXxxSlotFilter</c> properties) currently had selected, even
    /// when that slot is still perfectly validly configured (e.g. on every
    /// leader switch, not just when a slot is actually removed) - the exact
    /// race <see cref="RemoveSlotFromGroup"/>'s doc comment describes in
    /// detail for <see cref="SelectedChatSlot"/> specifically. Capturing the
    /// old selections before the Clear and explicitly restoring them
    /// afterward (only if the slot is still in <paramref name="ordered"/>  -
    /// i.e. wasn't actually removed, in which case null/"All slots" is the
    /// correct end state anyway) fixes this uniformly for all four
    /// dropdowns, regardless of why this rebuild was triggered - relying on
    /// <see cref="OnSelectedChatSlotChanged"/>'s "spurious null" guard alone
    /// only ever protected <see cref="SelectedChatSlot"/>, not the three
    /// slot filters, which have no such guard.
    /// </summary>
    private void RefreshSlotOrder()
    {
        var previousChatSlot = SelectedChatSlot;
        var previousEventsFilter = SelectedEventsSlotFilter;
        var previousHintsFilter = SelectedHintsSlotFilter;
        var previousItemsFilter = SelectedItemsSlotFilter;

        var ordered = Group.Slots
            .OrderBy(s => s.Id == LeaderSlotId ? 0 : 1)
            .ThenBy(s => s.SlotName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Slots.Clear();
        foreach (var slot in ordered)
        {
            Slots.Add(slot);
        }

        while (SlotFilterOptions.Count > 1)
        {
            SlotFilterOptions.RemoveAt(SlotFilterOptions.Count - 1);
        }

        foreach (var slot in ordered)
        {
            SlotFilterOptions.Add(slot);
        }

        // Slots.Count just changed (or this is the initial build) - re-evaluate
        // whether there's anything left to Connect as (see CanConnect).
        OnPropertyChanged(nameof(CanConnect));
        ConnectCommand.NotifyCanExecuteChanged();

        SelectedChatSlot = previousChatSlot is not null && ordered.Contains(previousChatSlot) ? previousChatSlot : null;
        SelectedEventsSlotFilter = previousEventsFilter is not null && ordered.Contains(previousEventsFilter) ? previousEventsFilter : null;
        SelectedHintsSlotFilter = previousHintsFilter is not null && ordered.Contains(previousHintsFilter) ? previousHintsFilter : null;
        SelectedItemsSlotFilter = previousItemsFilter is not null && ordered.Contains(previousItemsFilter) ? previousItemsFilter : null;
    }

    partial void OnGroupChanged(ServerConnectionGroup? oldValue, ServerConnectionGroup newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnGroupPropertyChanged;
        }

        newValue.PropertyChanged += OnGroupPropertyChanged;
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(Slots));
    }

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsLeaderConnected));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanConnect));
        SendMessageCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnLeaderSlotIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsLeaderConnected));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(CanConnect));
        SendMessageCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();

        // The leader just changed, so it might need to move to the front of
        // Slots/SlotFilterOptions - see RefreshSlotOrder.
        RefreshSlotOrder();
    }

    /// <summary>
    /// The one action that starts a disconnect/reconnect: picking a
    /// different account to chat as. Ignored while a previous switch is
    /// still being applied to avoid re-entering the switch for the same
    /// change twice (see <see cref="ApplyLeaderSelectionAsync"/>, which
    /// sets/clears <see cref="_applyingLeaderChange"/> around the actual
    /// await). A <paramref name="newValue"/> of null here is never a real
    /// user selection (the dropdown's ItemsSource never contains a null
    /// entry) - it's a spurious rebinding artifact, e.g. when a tab's
    /// visual tree is reattached on tab switch. Restoring the previous
    /// value instead of disconnecting is what keeps an already-connected
    /// tab connected across tab switches; use the actual Disconnect
    /// button/command for a real disconnect.
    /// </summary>
    partial void OnSelectedChatSlotChanged(SlotProfile? oldValue, SlotProfile? newValue)
    {
        if (_applyingLeaderChange)
        {
            return;
        }

        if (newValue is null)
        {
            _applyingLeaderChange = true;
            try
            {
                SelectedChatSlot = oldValue;
            }
            finally
            {
                _applyingLeaderChange = false;
            }

            return;
        }

        _ = ApplyLeaderSelectionAsync(newValue);
    }

    private async Task ApplyLeaderSelectionAsync(SlotProfile targetSlot)
    {
        _applyingLeaderChange = true;
        try
        {
            await _connectionManager.SwitchLeaderAsync(this, targetSlot);
        }
        finally
        {
            _applyingLeaderChange = false;
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        UnreadEventCount = 0;

        // Tier 2 progress is fetched lazily rather than on some background
        // timer for every group regardless of whether its tab is even being
        // looked at - the first time a tab with a resolved tracker id is
        // actually selected is enough (the static half of the data barely
        // ever changes within a room's lifetime anyway - see
        // Feature-Plaene/Archiv/Fortschrittsanzeigen.md). A later manual refresh
        // (see RefreshMultiworldProgressCommand) still respects the tracker
        // service's own cache timers regardless of how often this fires.
        if (HasMultiworldTracker && MultiworldProgress.Count == 0 && !IsRefreshingMultiworldProgress)
        {
            _ = RefreshMultiworldProgressAsync();
        }
    }

    partial void OnUnreadEventCountChanged(int value) => OnPropertyChanged(nameof(HasUnreadEvents));

    partial void OnSelectedHintFilterChanged(HintFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedHintRoleFilterChanged(HintRoleFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedHintItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedItemCategoryFilterChanged(ItemCategoryFilter value) => OnPropertyChanged(nameof(VisibleReceivedItems));

    partial void OnSelectedEventsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnSelectedHintsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleHints));

    partial void OnSelectedItemsSlotFilterChanged(SlotProfile? value) => OnPropertyChanged(nameof(VisibleReceivedItems));

    partial void OnItemSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(VisibleReceivedItems));
    }

    partial void OnSelectedEventRelevanceFilterChanged(EventRelevanceFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnSelectedEventCategoryFilterChanged(EventCategoryFilter value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnShowProgressionItemEventsChanged(bool value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnShowUsefulItemEventsChanged(bool value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnShowFillerItemEventsChanged(bool value) => OnPropertyChanged(nameof(VisibleEvents));

    partial void OnShowTrapItemEventsChanged(bool value) => OnPropertyChanged(nameof(VisibleEvents));

    private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(VisibleEvents));

        if (e.Action != NotifyCollectionChangedAction.Add || IsSelected || e.NewItems is null)
        {
            return;
        }

        var relevantCount = 0;
        foreach (var item in e.NewItems)
        {
            if (item is EventEntry { ConcernsOwnSlot: true })
            {
                relevantCount++;
            }
        }

        UnreadEventCount += relevantCount;
    }

    private void OnHintsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is HintEntry hint)
                {
                    hint.PropertyChanged += OnHintEntryPropertyChanged;
                }
            }
        }

        RaiseHintAggregatesChanged();
    }

    private void OnHintEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HintEntry.Found))
        {
            RaiseHintAggregatesChanged();
        }
    }

    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerConnectionGroup.Name))
        {
            OnPropertyChanged(nameof(HeaderText));
        }

        if (e.PropertyName == nameof(ServerConnectionGroup.TrackerId))
        {
            OnPropertyChanged(nameof(HasMultiworldTracker));

            // A freshly (re-)configured tracker id has no data yet - and an
            // id that just got cleared should stop showing stale progress
            // from whatever room it used to point at.
            MultiworldProgressError = null;
            MultiworldProgress.Clear();
            RaiseCombinedProgressChanged();

            if (HasMultiworldTracker && IsSelected)
            {
                _ = RefreshMultiworldProgressAsync();
            }
        }
    }

    private void RaiseHintAggregatesChanged()
    {
        OnPropertyChanged(nameof(VisibleHints));
        OnPropertyChanged(nameof(UnfoundHintCount));
        OnPropertyChanged(nameof(UnfoundHintIFindCount));
        OnPropertyChanged(nameof(UnfoundHintIReceiveCount));
        OnPropertyChanged(nameof(HintsPanelButtonText));
        OnPropertyChanged(nameof(HintsIFindButtonText));
        OnPropertyChanged(nameof(HintsIReceiveButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() =>
        // ConnectionManager.DisconnectGroupAsync itself calls
        // SetLeaderStateWithoutTriggeringSwitch(null, null) once the
        // disconnect completes, which resets SelectedChatSlot (so the
        // dropdown reflects "not connected") without re-entering
        // OnSelectedChatSlotChanged - nothing more to do here.
        _connectionManager.DisconnectGroupAsync(this);

    /// <summary>
    /// The explicit "get connected" action shown in place of Disconnect
    /// while the group is offline (see <see cref="CanConnect"/>) - picking a
    /// leader used to be possible only via the "Chat as" dropdown further
    /// down (see <see cref="OnSelectedChatSlotChanged"/>), which isn't
    /// discoverable as *the* way to connect at all when nothing is connected
    /// yet. Picks the same slot startup would (see
    /// <see cref="IConnectionManager.InitializeGroupAsync"/>): the group's
    /// remembered <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/>
    /// if it still refers to a configured slot, else the first configured
    /// slot. <see cref="SwitchLeaderAsync"/> itself sets both
    /// AutoConnect/PreferredLeaderSlotId and triggers the sibling catch-up
    /// sweep, same as connecting via the dropdown would.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync()
    {
        var preferredId = Group.PreferredLeaderSlotId;
        var targetSlot = (preferredId is not null ? Group.Slots.FirstOrDefault(s => s.Id == preferredId.Value) : null)
                          ?? Group.Slots.FirstOrDefault();

        return targetSlot is null ? Task.CompletedTask : _connectionManager.SwitchLeaderAsync(this, targetSlot);
    }

    [RelayCommand(CanExecute = nameof(IsLeaderConnected))]
    private async Task SendMessageAsync()
    {
        var text = MessageToSend;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageToSend = string.Empty;
        RecordSentMessage(text);
        await _connectionManager.SendMessageAsync(this, text);
    }

    /// <summary>
    /// Appends a just-sent message to the recall history (see
    /// <see cref="_messageHistory"/>'s doc comment) and resets navigation -
    /// every send starts the next Up-press fresh from the newest entry.
    /// Deliberately doesn't dedupe against the previous entry: resending the
    /// same !hint text several times in a row is the main use case, and each
    /// send should still get its own history slot to step through.
    /// </summary>
    private void RecordSentMessage(string text)
    {
        _messageHistory.Add(text);
        if (_messageHistory.Count > MaxMessageHistory)
        {
            _messageHistory.RemoveAt(0);
        }

        _messageHistoryIndex = -1;
        _messageHistoryDraft = null;
    }

    /// <summary>
    /// Steps one message further back in the recall history (Up arrow) -
    /// called from MainWindow.axaml.cs's message TextBox key handler. On the
    /// first press, stashes whatever the user had already typed so
    /// <see cref="RecallNextMessage"/> can hand it back later; further
    /// presses just walk further back, stopping at the oldest entry.
    /// </summary>
    public void RecallPreviousMessage()
    {
        if (_messageHistory.Count == 0)
        {
            return;
        }

        if (_messageHistoryIndex == -1)
        {
            _messageHistoryDraft = MessageToSend;
            _messageHistoryIndex = _messageHistory.Count - 1;
        }
        else if (_messageHistoryIndex > 0)
        {
            _messageHistoryIndex--;
        }

        MessageToSend = _messageHistory[_messageHistoryIndex];
    }

    /// <summary>
    /// Steps one message forward through the recall history (Down arrow) -
    /// the mirror of <see cref="RecallPreviousMessage"/>. Once it steps past
    /// the newest entry, restores whatever the user had originally typed
    /// before they started navigating, and leaves history navigation.
    /// No-op if history navigation isn't currently active.
    /// </summary>
    public void RecallNextMessage()
    {
        if (_messageHistoryIndex == -1)
        {
            return;
        }

        if (_messageHistoryIndex < _messageHistory.Count - 1)
        {
            _messageHistoryIndex++;
            MessageToSend = _messageHistory[_messageHistoryIndex];
        }
        else
        {
            _messageHistoryIndex = -1;
            MessageToSend = _messageHistoryDraft ?? string.Empty;
            _messageHistoryDraft = null;
        }
    }

    [RelayCommand]
    private void ShowAllHints() => SelectedHintFilter = HintFilter.All;

    [RelayCommand]
    private void ShowUnfoundHints() => SelectedHintFilter = HintFilter.Unfound;

    [RelayCommand]
    private void ShowHintsPanel() => SelectedRightPanel = RightPanelView.Hints;

    [RelayCommand]
    private void ShowReceivedItemsPanel() => SelectedRightPanel = RightPanelView.ReceivedItems;

    [RelayCommand]
    private void ShowAllItemCategories() => SelectedItemCategoryFilter = ItemCategoryFilter.All;

    [RelayCommand]
    private void ShowProgressItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Progress;

    [RelayCommand]
    private void ShowUsefulItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Useful;

    [RelayCommand]
    private void ShowNormalItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Normal;

    [RelayCommand]
    private void ShowTrapItems() => SelectedItemCategoryFilter = ItemCategoryFilter.Trap;

    [RelayCommand]
    private void ShowAllHintRoles() => SelectedHintRoleFilter = HintRoleFilter.All;

    [RelayCommand]
    private void ShowHintsIFind() => SelectedHintRoleFilter = HintRoleFilter.IFind;

    [RelayCommand]
    private void ShowHintsIReceive() => SelectedHintRoleFilter = HintRoleFilter.IReceive;

    [RelayCommand]
    private void ShowAllHintItemCategories() => SelectedHintItemCategoryFilter = ItemCategoryFilter.All;

    [RelayCommand]
    private void ShowProgressHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Progress;

    [RelayCommand]
    private void ShowUsefulHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Useful;

    [RelayCommand]
    private void ShowNormalHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Normal;

    [RelayCommand]
    private void ShowTrapHintItems() => SelectedHintItemCategoryFilter = ItemCategoryFilter.Trap;

    [RelayCommand]
    private void ShowAllEventsRelevance() => SelectedEventRelevanceFilter = EventRelevanceFilter.All;

    [RelayCommand]
    private void ShowOwnEventsOnly() => SelectedEventRelevanceFilter = EventRelevanceFilter.ConcernsMe;

    [RelayCommand]
    private void ShowAllEventCategories() => SelectedEventCategoryFilter = EventCategoryFilter.All;

    [RelayCommand]
    private void ShowHintEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Hints;

    [RelayCommand]
    private void ShowItemEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Items;

    [RelayCommand]
    private void ShowChatEventsOnly() => SelectedEventCategoryFilter = EventCategoryFilter.Chat;

    /// <summary>
    /// Sets <see cref="SelectedChatSlot"/>/<see cref="LeaderSlotId"/> without
    /// running <see cref="OnSelectedChatSlotChanged"/>'s switch logic - used
    /// by <see cref="Services.ConnectionManager"/> itself once a switch has
    /// actually completed (or failed), so the dropdown reflects reality
    /// instead of assuming the requested switch always succeeds.
    /// </summary>
    public void SetLeaderStateWithoutTriggeringSwitch(Guid? leaderSlotId, SlotProfile? chatSlot)
    {
        _applyingLeaderChange = true;
        try
        {
            LeaderSlotId = leaderSlotId;
            SelectedChatSlot = chatSlot;
        }
        finally
        {
            _applyingLeaderChange = false;
        }
    }
}
