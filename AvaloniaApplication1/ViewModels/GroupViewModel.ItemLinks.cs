using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Archipolygo.Models;
using CommunityToolkit.Mvvm.Input;

namespace Archipolygo.ViewModels;

/// <summary>The room's item-link groups and which slots share each of them.</summary>
public partial class GroupViewModel
{
    public ObservableCollection<ItemLinkGroup> ItemLinkGroups { get; } = new();

    public bool HasItemLinkGroups => ItemLinkGroups.Count > 0;

    /// <summary>Replaces the whole list; item links are fixed per seed, so one read per leader connect is enough.</summary>
    public void ApplyItemLinkGroups(IEnumerable<ItemLinkGroupSnapshot> snapshots)
    {
        foreach (var previous in ItemLinkGroups)
        {
            previous.Slot.ItemLinkMembersText = null;
        }

        ItemLinkGroups.Clear();
        foreach (var snapshot in snapshots.OrderBy(s => s.Group.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var members = snapshot.Members
                .Select(member => new ItemLinkMember(member.DisplayName, ClassifyItemLinkMember(member), GetRoomSlotStatus(member.SlotNumber)))
                .OrderByDescending(member => member.Kind == EventTextSegmentKind.OwnSlotName)
                .ThenBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groupStatus = GetRoomSlotStatus(snapshot.Group.SlotNumber);
            groupStatus.ItemLinkMembersText = "Item link: " + string.Join(", ", members.Select(m => m.DisplayName));
            ItemLinkGroups.Add(new ItemLinkGroup(snapshot.Group.DisplayName, snapshot.Game, groupStatus, members));
        }

        OnPropertyChanged(nameof(HasItemLinkGroups));
        if (!HasItemLinkGroups && SelectedRightPanel == RightPanelView.ItemLinks)
        {
            SelectedRightPanel = RightPanelView.Hints;
        }
    }

    private EventTextSegmentKind ClassifyItemLinkMember(RoomPlayerRef member) =>
        Group.Slots.Any(slot => string.Equals(slot.SlotName, member.SlotName, StringComparison.OrdinalIgnoreCase))
            ? EventTextSegmentKind.OwnSlotName
            : EventTextSegmentKind.OtherSlotName;

    [RelayCommand]
    private void ShowItemLinksPanel() => SelectedRightPanel = RightPanelView.ItemLinks;
}
