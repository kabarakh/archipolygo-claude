using Archipolygo.Models;
using Archipolygo.Services;

namespace AvaloniaApplication1.Tests;

public class ConfigImportMergerTests
{
    private static ServerConnectionGroup Group(string host, int port, params string[] slotNames)
    {
        var group = new ServerConnectionGroup { Host = host, Port = port, Name = $"{host}:{port}" };
        foreach (var name in slotNames)
        {
            group.Slots.Add(new SlotProfile { GroupId = group.Id, SlotName = name });
        }

        return group;
    }

    private static SlotProfile Slot(ServerConnectionGroup group, string name) =>
        group.Slots.Single(s => s.SlotName == name);

    [Fact]
    public void GroupWithoutLocalMatch_IsAddedAsNewWithItsOwnIds()
    {
        var local = Group("archipelago.gg", 1000, "Alice");
        var imported = Group("archipelago.gg", 2000, "Bob");

        var plan = ConfigImportMerger.Merge([local], [imported]);

        var added = Assert.Single(plan.NewGroups);
        Assert.Empty(plan.Overwrites);
        Assert.Empty(plan.SlotIdsWithObsoleteSyncState);
        Assert.Equal(imported.Id, added.Id);
        var slot = Assert.Single(added.Slots);
        Assert.Equal(Slot(imported, "Bob").Id, slot.Id);
        Assert.Equal(added.Id, slot.GroupId);
    }

    [Fact]
    public void GroupsMatchOnHostIgnoringCaseAndPort()
    {
        var local = Group("Archipelago.GG", 38281);
        var imported = Group("archipelago.gg", 38281);

        var plan = ConfigImportMerger.Merge([local], [imported]);

        Assert.Empty(plan.NewGroups);
        Assert.Same(local, Assert.Single(plan.Overwrites).Local);
    }

    [Fact]
    public void SameHostWithDifferentPort_IsNotAMatch()
    {
        var plan = ConfigImportMerger.Merge([Group("host", 1)], [Group("host", 2)]);

        Assert.Single(plan.NewGroups);
        Assert.Empty(plan.Overwrites);
    }

    [Fact]
    public void OverwrittenGroup_KeepsLocalIdButTakesEverythingElseFromImport()
    {
        var local = Group("host", 1);
        local.AutoConnect = true;
        local.Color = "Red";
        var imported = new ServerConnectionGroup
        {
            Host = "HOST",
            Port = 1,
            Name = "Imported name",
            AutoConnect = false,
            NotificationsMuted = true,
            IsAdmin = true,
            Color = "Blue",
            TrackerReferenceInput = "https://archipelago.gg/tracker/abc",
            TrackerId = "abc",
        };

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.Equal(local.Id, replacement.Id);
        Assert.Equal("HOST", replacement.Host);
        Assert.Equal("Imported name", replacement.Name);
        Assert.False(replacement.AutoConnect);
        Assert.True(replacement.NotificationsMuted);
        Assert.True(replacement.IsAdmin);
        Assert.Equal("Blue", replacement.Color);
        Assert.Equal("https://archipelago.gg/tracker/abc", replacement.TrackerReferenceInput);
        Assert.Equal("abc", replacement.TrackerId);
    }

    [Fact]
    public void AutoConnectComesFromImportEvenWhenLocalHadItOff()
    {
        var local = Group("host", 1);
        var imported = Group("host", 1);
        imported.AutoConnect = true;

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.True(replacement.AutoConnect);
    }

    [Fact]
    public void MatchedSlot_KeepsLocalIdAndTakesOtherDataFromImport()
    {
        var local = Group("host", 1, "Alice");
        var imported = Group("host", 1, "ALICE");
        var importedSlot = Slot(imported, "ALICE");
        importedSlot.RequiresPassword = true;
        importedSlot.Alias = "Ally";
        importedSlot.LocationsChecked = 5;
        importedSlot.LocationsTotal = 10;

        var plan = ConfigImportMerger.Merge([local], [imported]);

        var replacement = Assert.Single(plan.Overwrites).Replacement;
        var slot = Assert.Single(replacement.Slots);
        Assert.Equal(Slot(local, "Alice").Id, slot.Id);
        Assert.Equal(local.Id, slot.GroupId);
        Assert.Equal("ALICE", slot.SlotName);
        Assert.True(slot.RequiresPassword);
        Assert.Equal("Ally", slot.Alias);
        Assert.Equal(5, slot.LocationsChecked);
        Assert.Equal(10, slot.LocationsTotal);
        Assert.Empty(plan.SlotIdsWithObsoleteSyncState);
    }

    [Fact]
    public void SlotOnlyInLocal_IsDroppedAndItsSyncStateMarkedObsolete()
    {
        var local = Group("host", 1, "Alice", "Carol");
        var imported = Group("host", 1, "Alice");

        var plan = ConfigImportMerger.Merge([local], [imported]);

        Assert.DoesNotContain(Assert.Single(plan.Overwrites).Replacement.Slots, s => s.SlotName == "Carol");
        Assert.Equal([Slot(local, "Carol").Id], plan.SlotIdsWithObsoleteSyncState);
    }

    [Fact]
    public void SlotOnlyInImport_IsAddedWithIdFromFile()
    {
        var local = Group("host", 1, "Alice");
        var imported = Group("host", 1, "Alice", "Bob");

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        var bob = replacement.Slots.Single(s => s.SlotName == "Bob");
        Assert.Equal(Slot(imported, "Bob").Id, bob.Id);
        Assert.Equal(local.Id, bob.GroupId);
    }

    [Fact]
    public void PreferredLeader_IsRemappedToLocalIdOfMatchedSlot()
    {
        var local = Group("host", 1, "Alice", "Bob");
        var imported = Group("host", 1, "Alice", "bob");
        imported.PreferredLeaderSlotId = Slot(imported, "bob").Id;

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.Equal(Slot(local, "Bob").Id, replacement.PreferredLeaderSlotId);
        Assert.Equal("bob", replacement.ResolveStartupLeader()!.SlotName);
    }

    [Fact]
    public void PreferredLeaderPointingAtNoImportedSlot_BecomesNull()
    {
        var imported = Group("host", 1, "Alice");
        imported.PreferredLeaderSlotId = Guid.NewGuid();

        var added = Assert.Single(ConfigImportMerger.Merge([], [imported]).NewGroups);

        Assert.Null(added.PreferredLeaderSlotId);
    }

    [Fact]
    public void NewGroupWhoseIdCollidesWithUnrelatedLocalGroup_GetsFreshIdIncludingOnItsSlots()
    {
        var local = Group("host", 1);
        var imported = Group("other", 2, "Bob");
        imported.Id = local.Id;

        var added = Assert.Single(ConfigImportMerger.Merge([local], [imported]).NewGroups);

        Assert.NotEqual(local.Id, added.Id);
        Assert.Equal(added.Id, Assert.Single(added.Slots).GroupId);
    }

    [Fact]
    public void ImportedSlotIdCollidingWithUnrelatedLocalSlot_GetsFreshIdAndLeaderFollows()
    {
        var local = Group("host", 1, "Alice");
        var imported = Group("other", 2, "Bob");
        Slot(imported, "Bob").Id = Slot(local, "Alice").Id;
        imported.PreferredLeaderSlotId = Slot(imported, "Bob").Id;

        var added = Assert.Single(ConfigImportMerger.Merge([local], [imported]).NewGroups);

        var bob = Assert.Single(added.Slots);
        Assert.NotEqual(Slot(local, "Alice").Id, bob.Id);
        Assert.Equal(bob.Id, added.PreferredLeaderSlotId);
    }

    [Fact]
    public void ImportedSlotIdCollidingWithDroppedLocalSlot_GetsFreshId()
    {
        var local = Group("host", 1, "Carol");
        var imported = Group("host", 1, "Bob");
        Slot(imported, "Bob").Id = Slot(local, "Carol").Id;

        var plan = ConfigImportMerger.Merge([local], [imported]);

        var bob = Assert.Single(Assert.Single(plan.Overwrites).Replacement.Slots);
        Assert.NotEqual(Slot(local, "Carol").Id, bob.Id);
        Assert.Equal([Slot(local, "Carol").Id], plan.SlotIdsWithObsoleteSyncState);
    }

    [Fact]
    public void InMemoryPasswords_OfOverwrittenGroupAndMatchedSlots_AreKept()
    {
        var local = Group("host", 1, "Alice");
        local.Password = "room";
        Slot(local, "Alice").Password = "slot";
        var imported = Group("host", 1, "Alice", "Bob");

        var replacement = Assert.Single(ConfigImportMerger.Merge([local], [imported]).Overwrites).Replacement;

        Assert.Equal("room", replacement.Password);
        Assert.Equal("slot", Slot(replacement, "Alice").Password);
        Assert.Null(Slot(replacement, "Bob").Password);
    }

    [Fact]
    public void Merge_MutatesNeitherLocalNorImportedGroups()
    {
        var local = Group("host", 1, "Alice", "Carol");
        var imported = Group("host", 1, "alice", "Bob");
        imported.Name = "Imported";
        var localSlotsBefore = local.Slots.ToList();
        var importedAliceId = Slot(imported, "alice").Id;

        var plan = ConfigImportMerger.Merge([local], [imported]);

        Assert.Equal(localSlotsBefore, local.Slots);
        Assert.Equal("host:1", local.Name);
        Assert.Equal(importedAliceId, Slot(imported, "alice").Id);
        Assert.Equal(imported.Id, Slot(imported, "alice").GroupId);
        Assert.DoesNotContain(plan.Overwrites[0].Replacement.Slots, s => local.Slots.Contains(s) || imported.Slots.Contains(s));
    }

    [Fact]
    public void TwoImportedGroupsForSameServer_OnlyFirstOverwrites()
    {
        var local = Group("host", 1);
        var first = Group("host", 1);
        var second = Group("host", 1);

        var plan = ConfigImportMerger.Merge([local], [first, second]);

        Assert.Single(plan.Overwrites);
        Assert.Equal(second.Id, Assert.Single(plan.NewGroups).Id);
    }
}
