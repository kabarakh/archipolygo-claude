using System;
using System.Collections.Generic;
using System.Linq;
using Archipolygo.Models;

namespace Archipolygo.Services;

/// <summary>An imported group that replaces <paramref name="Local"/>; <paramref name="Replacement"/> keeps Local's Id.</summary>
public sealed record GroupOverwrite(ServerConnectionGroup Local, ServerConnectionGroup Replacement);

public sealed record ConfigImportPlan(
    IReadOnlyList<ServerConnectionGroup> NewGroups,
    IReadOnlyList<GroupOverwrite> Overwrites,
    IReadOnlyList<Guid> SlotIdsWithObsoleteSyncState);

/// <summary>
/// Decides how imported groups map onto the local ones. Pure: neither input is mutated, every group and slot in the
/// plan is a fresh copy, so the caller can disconnect first and swap them in afterwards.
/// See Config-Verwaltung.md, "Überschreiben einer vorhandenen Gruppe".
/// </summary>
public static class ConfigImportMerger
{
    public static ConfigImportPlan Merge(
        IReadOnlyCollection<ServerConnectionGroup> localGroups,
        IReadOnlyCollection<ServerConnectionGroup> importedGroups)
    {
        var claimedGroupIds = localGroups.Select(g => g.Id).ToHashSet();
        var claimedSlotIds = localGroups.SelectMany(g => g.Slots).Select(s => s.Id).ToHashSet();
        var overwrittenLocals = new HashSet<ServerConnectionGroup>();

        var newGroups = new List<ServerConnectionGroup>();
        var overwrites = new List<GroupOverwrite>();
        var obsoleteSlotIds = new List<Guid>();

        foreach (var imported in importedGroups)
        {
            // A second imported group for the same server becomes a new group rather than overwriting the first.
            var local = localGroups.FirstOrDefault(l => !overwrittenLocals.Contains(l) && IsSameServer(l, imported));
            if (local is null)
            {
                var groupId = ClaimUnusedId(imported.Id, claimedGroupIds);
                newGroups.Add(CopyGroup(imported, groupId, _ => null, claimedSlotIds));
                continue;
            }

            overwrittenLocals.Add(local);
            var replacement = CopyGroup(imported, local.Id, MatchingLocalSlot, claimedSlotIds);
            replacement.Password = local.Password;
            overwrites.Add(new GroupOverwrite(local, replacement));
            obsoleteSlotIds.AddRange(local.Slots
                .Where(s => !imported.Slots.Any(i => IsSameSlot(s, i)))
                .Select(s => s.Id));

            SlotProfile? MatchingLocalSlot(SlotProfile importedSlot) =>
                local.Slots.FirstOrDefault(s => IsSameSlot(s, importedSlot));
        }

        return new ConfigImportPlan(newGroups, overwrites, obsoleteSlotIds);
    }

    public static bool IsSameServer(ServerConnectionGroup a, ServerConnectionGroup b) =>
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    private static bool IsSameSlot(SlotProfile a, SlotProfile b) =>
        string.Equals(a.SlotName, b.SlotName, StringComparison.OrdinalIgnoreCase);

    /// <summary>An imported Id that already belongs to something unrelated would share its sync-state or cache.</summary>
    private static Guid ClaimUnusedId(Guid wanted, HashSet<Guid> claimed)
    {
        var id = claimed.Contains(wanted) ? Guid.NewGuid() : wanted;
        claimed.Add(id);
        return id;
    }

    private static ServerConnectionGroup CopyGroup(
        ServerConnectionGroup imported,
        Guid groupId,
        Func<SlotProfile, SlotProfile?> findLocalSlot,
        HashSet<Guid> claimedSlotIds)
    {
        var copy = new ServerConnectionGroup
        {
            Id = groupId,
            Name = imported.Name,
            Host = imported.Host,
            Port = imported.Port,
            AutoConnect = imported.AutoConnect,
            NotificationsMuted = imported.NotificationsMuted,
            IsAdmin = imported.IsAdmin,
            Color = imported.Color,
            TrackerReferenceInput = imported.TrackerReferenceInput,
            TrackerId = imported.TrackerId,
        };

        var finalIdByImportedId = new Dictionary<Guid, Guid>();
        foreach (var importedSlot in imported.Slots)
        {
            var localSlot = findLocalSlot(importedSlot);
            var slotId = localSlot?.Id ?? ClaimUnusedId(importedSlot.Id, claimedSlotIds);
            finalIdByImportedId.TryAdd(importedSlot.Id, slotId);
            copy.Slots.Add(CopySlot(importedSlot, slotId, groupId, localSlot?.Password));
        }

        copy.PreferredLeaderSlotId = imported.PreferredLeaderSlotId is { } preferred
            && finalIdByImportedId.TryGetValue(preferred, out var mapped)
                ? mapped
                : null;
        return copy;
    }

    /// <summary>Password is never persisted, so the import cannot carry one; keep whatever the local slot had in memory.</summary>
    private static SlotProfile CopySlot(SlotProfile imported, Guid slotId, Guid groupId, string? localPassword) => new()
    {
        Id = slotId,
        GroupId = groupId,
        SlotName = imported.SlotName,
        Password = localPassword,
        RequiresPassword = imported.RequiresPassword,
        Alias = imported.Alias,
        LocationsChecked = imported.LocationsChecked,
        LocationsTotal = imported.LocationsTotal,
    };
}
