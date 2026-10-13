using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.ViewModels;

/// <summary>The configured slots, their dropdown ordering and which one is the leader.</summary>
public partial class GroupViewModel
{
    /// <summary>Re-entrancy guard: suppresses <see cref="OnSelectedChatSlotChanged"/> while a leader change is being applied.</summary>
    private bool _applyingLeaderChange;

    /// <summary>The slot holding the one live connection for this server, or null.</summary>
    [ObservableProperty]
    private Guid? _leaderSlotId;

    /// <summary>
    /// The "Chat as" selection; changing it triggers a leader switch. Separate from
    /// <see cref="LeaderSlotId"/> so the dropdown doesn't flap while the handshake runs.
    /// </summary>
    [ObservableProperty]
    private SlotProfile? _selectedChatSlot;

    /// <summary>
    /// Configured slots for the "Chat as" dropdown: leader first, then alphabetical.
    /// Deliberately not a passthrough to <c>Group.Slots</c>, which keeps insertion order.
    /// </summary>
    public ObservableCollection<SlotProfile> Slots { get; } = new();

    /// <summary>
    /// <see cref="Slots"/> (same order) after a leading null = "All slots";
    /// the shared ItemsSource of the three slot filter dropdowns.
    /// </summary>
    public ObservableCollection<SlotProfile?> SlotFilterOptions { get; } = new() { null };

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

    /// <summary>Adds slots via <see cref="InsertSlotsInOrder"/> instead of a Clear+rebuild (see there for why).</summary>
    public void AddSlotsToGroup(IEnumerable<SlotProfile> slots)
    {
        var newSlots = slots as IReadOnlyCollection<SlotProfile> ?? slots.ToList();

        WithoutSlotsCollectionHandler(() =>
        {
            foreach (var slot in newSlots)
            {
                Group.Slots.Add(slot);
                SubscribeSlotProgress(slot);
            }
        });

        InsertSlotsInOrder(newSlots);
        RaiseRoomProgressChanged();
    }

    /// <summary>
    /// Inserts at the sorted position instead of Clear+rebuild: while <see cref="Slots"/> is
    /// briefly empty, the ComboBox pushes a nested null the guard swallows, permanently
    /// dropping <see cref="SelectedChatSlot"/>. A new slot is never the leader, so index 0 stays.
    /// </summary>
    private void InsertSlotsInOrder(IEnumerable<SlotProfile> newSlots)
    {
        foreach (var slot in newSlots.OrderBy(s => s.SlotName, StringComparer.OrdinalIgnoreCase))
        {
            // Index 0 belongs to the leader regardless of its name.
            var index = LeaderSlotId is not null && Slots.Count > 0 && Slots[0].Id == LeaderSlotId ? 1 : 0;

            while (index < Slots.Count &&
                   string.Compare(Slots[index].SlotName, slot.SlotName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                index++;
            }

            Slots.Insert(index, slot);
            SlotFilterOptions.Insert(index + 1, slot); // +1 for the leading "All slots" null.
        }
    }

    /// <summary>
    /// Targeted Remove instead of Clear+rebuild, so the selected leader never leaves the
    /// ItemsSource (same ComboBox race as <see cref="InsertSlotsInOrder"/>). Removing the
    /// leader itself is safe: the caller disconnects it first.
    /// </summary>
    public void RemoveSlotFromGroup(SlotProfile slot)
    {
        ClearSlotFiltersPointingAt(slot);

        WithoutSlotsCollectionHandler(() =>
        {
            Group.Slots.Remove(slot);
            UnsubscribeSlotProgress(slot);
        });

        Slots.Remove(slot);
        SlotFilterOptions.Remove(slot);
        RaiseRoomProgressChanged();
    }

    /// <summary>A removed slot must not stay selected in any of the three slot filters.</summary>
    private void ClearSlotFiltersPointingAt(SlotProfile slot)
    {
        if (SelectedEventsSlotFilter == slot)
        {
            SelectedEventsSlotFilter = null;
        }

        if (SelectedHintsSlotFilter == slot)
        {
            SelectedHintsSlotFilter = null;
        }

        if (SelectedItemsSlotFilter == slot)
        {
            SelectedItemsSlotFilter = null;
        }
    }

    /// <summary>Mutates <c>Group.Slots</c> without triggering the full <see cref="RefreshSlotOrder"/>.</summary>
    private void WithoutSlotsCollectionHandler(Action mutate)
    {
        Group.Slots.CollectionChanged -= OnSlotsCollectionChanged;
        try
        {
            mutate();
        }
        finally
        {
            Group.Slots.CollectionChanged += OnSlotsCollectionChanged;
        }
    }

    /// <summary>
    /// Full re-sort (leader first, then alphabetical), needed when the leader changes.
    /// The Clear transiently empties the ItemsSources and drops all four dropdown selections,
    /// so they're captured beforehand and restored if the slot is still configured.
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

        OnPropertyChanged(nameof(CanConnect));
        ConnectCommand.NotifyCanExecuteChanged();

        SlotProfile? KeepIfStillConfigured(SlotProfile? slot) => slot is not null && ordered.Contains(slot) ? slot : null;

        SelectedChatSlot = KeepIfStillConfigured(previousChatSlot);
        SelectedEventsSlotFilter = KeepIfStillConfigured(previousEventsFilter);
        SelectedHintsSlotFilter = KeepIfStillConfigured(previousHintsFilter);
        SelectedItemsSlotFilter = KeepIfStillConfigured(previousItemsFilter);
    }

    partial void OnLeaderSlotIdChanged(Guid? value)
    {
        RaiseConnectionAvailabilityChanged();
        RefreshSlotOrder();
    }

    /// <summary>
    /// Picking another account switches the leader. A null is never a user choice but a
    /// spurious rebinding artifact (e.g. tab reattach) - restore instead of disconnecting.
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

    /// <summary>Lets ConnectionManager report the actual switch outcome without triggering another switch.</summary>
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
