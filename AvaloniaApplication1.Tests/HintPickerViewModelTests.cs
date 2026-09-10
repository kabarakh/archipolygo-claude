using System.Linq;
using Archipolygo.Models;
using Archipolygo.TestSupport;
using Archipolygo.ViewModels;

namespace AvaloniaApplication1.Tests;

/// <summary>
/// Kategorie A (Test-Umsetzungsplan.md): <see cref="HintPickerViewModel"/>'s
/// row-building/filtering logic - see Feature-Plaene/Archiv/Hint-Eingabefeld.md.
/// No session/Avalonia involved: Location mode is driven by
/// <see cref="FakeConnectionManager.SetHintableLocations"/> (already-completed
/// tasks, so <c>OnOpened</c>'s fire-and-forget refresh finishes synchronously
/// - same reasoning as <see cref="GroupViewModelRoomProgressTests"/> needing
/// no Avalonia dispatcher), Item mode reads straight off
/// <see cref="GroupViewModel.ReceivedItems"/>/<see cref="GroupViewModel.Hints"/>.
/// </summary>
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

    /// <summary>Builds a HintEntry the way ConnectionManager's hint-snapshot code would, given who's actually receiving/finding it - SlotId mirrors the real fallback (receiver if configured, else finder), so tests can reproduce the finder-fallback bug (see ItemMode_ExcludesHintWhereSlotIsOnlyTheFinder_NotTheReceiver).</summary>
    private static HintEntry Hint(SlotProfile owningSlot, string itemName, string receivingPlayerName, string findingPlayerName = "SomeFinder", bool found = false, string locationName = "Some Location") => new()
    {
        Key = System.Guid.NewGuid().ToString(),
        SlotId = owningSlot.Id,
        ReceivingPlayerName = receivingPlayerName,
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

    /// <summary>
    /// Dev-reported bug: a Kirby Super Star slot's item list showed items
    /// like "Memory of a Distant World" that only exist in a different,
    /// linked game's own pool. Root cause: HintEntry.SlotId is set to
    /// whichever configured slot a hint concerns, receiver *or* (only if the
    /// real receiver isn't configured here) finder - a hint where this slot
    /// is merely the finder is someone else's item, physically hidden in
    /// this slot's own world, not this slot's own item.
    /// </summary>
    [Fact]
    public void ItemMode_ExcludesHintWhereSlotIsOnlyTheFinder_NotTheReceiver()
    {
        var (viewModel, group, _) = MakeGroup("KabaKSS");
        var kss = group.Slots[0];

        // SlotId falls back to the finder (kss) because the true receiver
        // ("KabaXIV") isn't a slot configured in this app at all.
        viewModel.Hints.Add(Hint(kss, "Memory of a Distant World", receivingPlayerName: "KabaXIV", findingPlayerName: "KabaKSS"));

        viewModel.HintPicker.OnOpened();

        Assert.Empty(viewModel.HintPicker.FilteredRows);
    }

    [Fact]
    public void ItemMode_IncludesHintWhereSlotIsTheReceiver_ByAliasToo()
    {
        var (viewModel, group, _) = MakeGroup("KabaKSS");
        var kss = group.Slots[0];
        kss.Alias = "KabaDone";

        // ReceivingPlayerName comes back as the room alias (GetPlayerAlias),
        // not necessarily the raw slot name - must still match.
        viewModel.Hints.Add(Hint(kss, "Fire Rod", receivingPlayerName: "KabaDone", findingPlayerName: "SomeoneElse"));

        viewModel.HintPicker.OnOpened();

        Assert.Equal(new[] { "Fire Rod" }, viewModel.HintPicker.FilteredRows.Select(r => r.Name));
    }

    [Fact]
    public void ItemMode_ExcludeCheckbox_HidesOnlyFullyResolvedNames()
    {
        var (viewModel, group, _) = MakeGroup("Alice");
        var alice = group.Slots[0];

        // Received, no pending hint - nothing left to ask about.
        viewModel.ReceivedItems.Add(ReceivedItem(alice, "Compass"));

        // Never received, but a hint is out there (unfound) - still worth asking about? No:
        // this represents "I already know where a copy is" - stays visible either way since
        // it's not received yet.
        viewModel.Hints.Add(Hint(alice, "Map", receivingPlayerName: "Alice", found: false));

        // Received AND has its own still-unfound hint - a second copy may
        // still be worth asking about (dev feedback: several copies of a
        // named item can exist).
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

        Assert.Single(viewModel.HintPicker.FilteredRows); // stays visible - see class doc comment
        Assert.Contains((alice.Id, "Fire Rod"), manager.SentItemHints);
    }
}
