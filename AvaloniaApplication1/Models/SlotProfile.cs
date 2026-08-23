using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// A single slot on a <see cref="ServerConnectionGroup"/>. Since Phase 6,
/// this is (almost) all that actually differs between slots on the same
/// server - the old <c>ServerProfile</c> (Host, Port, SlotName, Password,
/// AutoConnect all on one record) is gone; Host/Port and (usually) Password
/// now live once on the group, and the group's
/// <see cref="ServerConnectionGroup.PreferredLeaderSlotId"/> replaces the old
/// per-slot AutoConnect flag. <see cref="Password"/> is the one exception -
/// a slot can still override the group's password for custom-hosted rooms
/// that need it.
/// </summary>
public partial class SlotProfile : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    /// <summary>Id of the <see cref="ServerConnectionGroup"/> this slot belongs to.</summary>
    [ObservableProperty]
    private Guid _groupId;

    [ObservableProperty]
    private string _slotName = string.Empty;

    /// <summary>
    /// Optional per-slot password override, used only when this slot's login
    /// password differs from the group's shared <see cref="ServerConnectionGroup.Password"/>
    /// - some custom-hosted Archipelago servers support a different password
    /// per slot. Null (the common case) means: use the group's password.
    /// </summary>
    [ObservableProperty]
    private string? _password;
}
