using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>Location mode uses already-completed fake tasks, so OnOpened's fire-and-forget refresh finishes synchronously.</summary>
public class HintPickerViewModelTests
{
    private static (GroupViewModel viewModel, ServerConnectionGroup group, FakeConnectionManager manager) MakeGroup(params string[] slotNames)
    {
        var manager = new FakeConnectionManager();
        var group = new ServerConnectionGroup { Name = "Test Server" };
        var viewModel = new GroupViewModel(group, manager);

        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return (viewModel, group, manager);
    }

    private static ReceivedItemEntry ReceivedItem(SlotProfile slot, string itemName) => new()
    {
        SlotId = slot.Id,
        ReceivingSlotName = slot.SlotName,
        ItemName = itemName,
        LocationName = "Somewhere",
        SenderName = "SomeSender",
        ItemKind = EventTextSegmentKind.ItemOther,
        SenderKind = EventTextSegmentKind.OtherSlotName,
    };

    /// <summary>SlotId mirrors the real fallback: receiver if configured, else finder.</summary>
    private static HintEntry Hint(SlotProfile owningSlot, string itemName, string receivingPlayerName, string findingPlayerName = "SomeFinder", bool found = false, string locationName = "Some Location", bool slotIsReceiver = true) => new()
    {
        Key = System.Guid.NewGuid().ToString(),
        SlotId = owningSlot.Id,
        ReceivingPlayerName = receivingPlayerName,
        ReceivingPlayerKind = slotIsReceiver ? EventTextSegmentKind.OwnSlotName : EventTextSegmentKind.OtherSlotName,
        FindingPlayerName = findingPlayerName,
        ItemName = itemName,
        LocationName = locationName,
        Found = found,
    };

    [Fact]
    public void DefaultMode_IsItem()
    {
        var (viewModel, _, _) = MakeGroup("Alice");
        Assert.Equal(HintTargetMode.Item, viewModel.HintPicker.Mode);
    }

    [Fact]
    public void ItemMode_CandidatePool_UnionsReceivedAndHintedNames_DedupedByName()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];

        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Small Key"));
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Small Key")); // a second copy - must not duplicate the row
        viewModel.Hints.Add(Hint(alice, "Fire Rod", receivingPlayerName: "Alice"));

        viewModel.HintPicker.OnOpened();

        var names = viewModel.HintPicker.FilteredRows.Select(r => r.Name).ToList();
        Assert.Equal(new[] { "Fire Rod", "Small Key" }, names); // alphabetical, one row per name
    }

    /// <summary>A hint where this slot is only the finder is someone else's item (e.g. from a linked game's pool).</summary>
    [Fact]
    public void ItemMode_ExcludesHintWhereSlotIsOnlyTheFinder_NotTheReceiver()
    {
        var (viewModel, group, _) = MakeGroup("KabaKSS");
        var kss = group.Slots[0];

        // SlotId falls back to the finder because the receiver isn't a configured slot.
        viewModel.Hints.Add(Hint(kss, "Memory of a Distant World", receivingPlayerName: "KabaXIV", findingPlayerName: "KabaKSS", slotIsReceiver: false));

        viewModel.HintPicker.OnOpened();

        Assert.Empty(viewModel.HintPicker.FilteredRows);
    }

    [Fact]
    public void ItemMode_IncludesHintWhereSlotIsTheReceiver_WhateverItsDisplayName()
    {
        var (viewModel, group, _) = MakeGroup("KabaKSS");
        var kss = group.Slots[0];

        // The slot's alias is not known yet, so the display name matches neither SlotName nor DisplayName.
        viewModel.Hints.Add(Hint(kss, "Fire Rod", receivingPlayerName: "KabaKSS (KabaDone)", findingPlayerName: "SomeoneElse"));

        viewModel.HintPicker.OnOpened();

        Assert.Equal(new[] { "Fire Rod" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void ItemMode_ExcludeCheckbox_HidesOnlyFullyResolvedNames()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];

        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Compass"));

        // Not received yet, so it stays visible despite the hint.
        viewModel.Hints.Add(Hint(alice, "Map", receivingPlayerName: "Alice", found: false));

        // Received but with its own unfound hint: several copies can exist.
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Bottle"));
        viewModel.Hints.Add(Hint(alice, "Bottle", receivingPlayerName: "Alice", found: false));

        viewModel.HintPicker.OnOpened();
        Assert.Equal(new[] { "Bottle", "Compass", "Map" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name).ToArray());

        viewModel.HintPicker.ExcludeAlreadyFoundOrHinted = true;

        var namesAfterExclude = viewModel.HintPicker.FilteredRows.Select(r => r.Name).ToArray();
        Assert.Contains("Map", namesAfterExclude);
        Assert.Contains("Bottle", namesAfterExclude);
        Assert.DoesNotContain("Compass", namesAfterExclude);
    }

    [Fact]
    public void ItemMode_CandidatePool_IncludesGameItemsNeverReceivedOrHinted()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];

        manager.SetHintableItems(alice, new[] { "Fire Rod", "Ice Rod", "Cane of Somaria" });
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));

        viewModel.HintPicker.OnOpened();

        Assert.Equal(
            new[] { "Cane of Somaria", "Fire Rod", "Ice Rod" },
            viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void ItemMode_ExcludeCheckbox_NeverHidesItemsNeverReceivedOrHinted()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];

        manager.SetHintableItems(alice, new[] { "Fire Rod", "Ice Rod", "Cane of Somaria" });
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod")); // the only one actually resolved

        viewModel.HintPicker.OnOpened();
        viewModel.HintPicker.ExcludeAlreadyFoundOrHinted = true;

        Assert.Equal(new[] { "Cane of Somaria", "Ice Rod" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void ItemMode_TogglingExcludeCheckbox_ReFiltersWithoutRefetching()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        manager.SetHintableItems(alice, new[] { "Fire Rod" });

        viewModel.HintPicker.OnOpened();
        Assert.Equal(1, manager.GetHintableItemsCallCount);

        viewModel.HintPicker.ExcludeAlreadyFoundOrHinted = true;
        viewModel.HintPicker.ExcludeAlreadyFoundOrHinted = false;

        Assert.Equal(1, manager.GetHintableItemsCallCount); // still just the one fetch from OnOpened()
    }

    [Fact]
    public void LocationMode_ExcludesAlreadyHintedLocations_ViaGroupHints()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];

        manager.SetHintableLocations(alice, new[]
        {
            new HintableLocation { LocationId = 1, Name = "Chest" },
            new HintableLocation { LocationId = 2, Name = "Well" },
        });
        viewModel.Hints.Add(Hint(alice, "Some Item", receivingPlayerName: "Alice", locationName: "Chest"));

        viewModel.HintPicker.Mode = HintTargetMode.Location;
        viewModel.HintPicker.OnOpened();

        Assert.Equal(new[] { "Well" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void LocationMode_NothingAvailable_HasNoRowsAtAllIsTrue()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        manager.SetHintableLocations(alice, new HintableLocation[0]);

        viewModel.HintPicker.Mode = HintTargetMode.Location;
        viewModel.HintPicker.OnOpened();

        Assert.True(viewModel.HintPicker.HasNoRowsAtAll);
        Assert.False(viewModel.HintPicker.HasNoSearchMatches); // distinct empty state - no rows at all, not "search matched nothing"
    }

    [Fact]
    public void SearchText_NarrowsFilteredRows_WithoutChangingHasNoRowsAtAll()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Ice Rod"));
        viewModel.HintPicker.OnOpened();

        viewModel.HintPicker.SearchText = "fire";

        Assert.Equal(new[] { "Fire Rod" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
        Assert.False(viewModel.HintPicker.HasNoRowsAtAll); // rows do exist overall
        Assert.False(viewModel.HintPicker.HasNoSearchMatches); // and this search did match something

        viewModel.HintPicker.SearchText = "nonexistent";
        Assert.Empty(viewModel.HintPicker.FilteredRows);
        Assert.True(viewModel.HintPicker.HasNoSearchMatches);
        Assert.False(viewModel.HintPicker.HasNoRowsAtAll);
    }

    [Fact]
    public void SwitchingSelectedSlot_ReloadsRowsForThatSlotOnly()
    {
        var (viewModel, group, _) = MakeGroup("Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Alice's Item"));
        viewModel.ReceivedItems.Add(ReceivedItem(bob, "Bob's Item"));

        viewModel.HintPicker.OnOpened(); // defaults to Alice (no leader connected -> first slot)
        Assert.Equal(new[] { "Alice's Item" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));

        viewModel.HintPicker.SelectedSlot = bob;
        Assert.Equal(new[] { "Bob's Item" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void SendCommand_LocationMode_RemovesRowImmediately_AndForwardsToConnectionManager()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        manager.SetHintableLocations(alice, new[] { new HintableLocation { LocationId = 42, Name = "Chest" } });
        viewModel.HintPicker.Mode = HintTargetMode.Location;
        viewModel.HintPicker.OnOpened();

        viewModel.HintPicker.SendCommand.Execute(viewModel.HintPicker.FilteredRows.Single());

        Assert.Empty(viewModel.HintPicker.FilteredRows); // gone immediately, no re-open needed
        Assert.Contains((alice.Id, 42L), manager.SentLocationHints);
    }

    [Fact]
    public void SendCommand_ItemMode_LeavesRowInPlace_AndForwardsToConnectionManager()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Fire Rod"));
        viewModel.HintPicker.OnOpened();

        viewModel.HintPicker.SendCommand.Execute(viewModel.HintPicker.FilteredRows.Single());

        Assert.Single(viewModel.HintPicker.FilteredRows); // stays visible
        Assert.Contains((alice.Id, "Fire Rod"), manager.SentItemHints);
    }

    [Fact]
    public void LoadingText_MatchesTheCurrentMode()
    {
        var (viewModel, _, _) = MakeGroup("Alice");

        Assert.Equal(HintTargetMode.Item, viewModel.HintPicker.Mode); // default
        Assert.Equal("Loading items...", viewModel.HintPicker.LoadingText);

        viewModel.HintPicker.Mode = HintTargetMode.Location;
        Assert.Equal("Loading locations...", viewModel.HintPicker.LoadingText);
    }

    /// <summary>Not on the first selection (nothing to release) and never the newly selected slot.</summary>
    [Fact]
    public void SwitchingSelectedSlot_ReleasesThePreviousSlotOnly()
    {
        var (viewModel, group, manager) = MakeGroup("Alice", "Bob");
        var alice = group.Slots[0];
        var bob = group.Slots[1];

        viewModel.HintPicker.OnOpened(); // first-ever selection - nothing to release
        Assert.Empty(manager.ReleasedHeldSessionSlots);

        viewModel.HintPicker.SelectedSlot = bob;

        Assert.Equal(new[] { alice }, manager.ReleasedHeldSessionSlots);
    }

    [Fact]
    public async Task OnClosedAsync_ReleasesTheCurrentlySelectedSlot()
    {
        var (viewModel, group, manager) = MakeGroup("Alice");
        var alice = group.Slots[0];
        viewModel.HintPicker.OnOpened();

        await viewModel.HintPicker.OnClosedAsync();

        Assert.Equal(new[] { alice }, manager.ReleasedHeldSessionSlots);
    }

    [Fact]
    public async Task OnClosedAsync_NoSlotEverSelected_DoesNotCallRelease()
    {
        var (viewModel, _, manager) = MakeGroup(); // no slots at all - SelectedSlot stays null

        await viewModel.HintPicker.OnClosedAsync();

        Assert.Empty(manager.ReleasedHeldSessionSlots);
    }
}
