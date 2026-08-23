using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// A single Archipelago server (Host+Port+Password), and every slot the user
/// has configured on it. Introduced in Phase 6: previously each slot got its
/// own independent <c>ServerProfile</c> (Host+Port+SlotName+Password all on
/// one record) with its own connection; now Host/Port/Password live once per
/// server, and <see cref="Slots"/> hold only what actually differs between
/// slots on the same server (the slot name). At most one slot per group is
/// ever the persistently-connected "leader" at a time - see
/// <see cref="Services.ConnectionManager"/>.
/// </summary>
public partial class ServerConnectionGroup : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = "archipelago.gg";

    [ObservableProperty]
    private int _port = 38281;

    /// <summary>Room password, shared by every slot on this server.</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    /// <summary>
    /// Whether this group should connect its leader automatically at
    /// startup, and try to bring the leader back (as whichever slot was
    /// actually connected) after an unexpected drop. Group-level equivalent
    /// of the old per-profile <c>AutoConnect</c> flag - since only one
    /// connection per group exists at a time, this can no longer be a
    /// per-slot setting.
    /// </summary>
    [ObservableProperty]
    private bool _autoConnect;

    /// <summary>
    /// Id of the <see cref="SlotProfile"/> that should become the leader at
    /// startup, if <see cref="AutoConnect"/> is set. Falls back to the first
    /// configured slot if unset (e.g. right after adding the first slot to a
    /// brand-new group).
    /// </summary>
    [ObservableProperty]
    private Guid? _preferredLeaderSlotId;

    /// <summary>
    /// Every slot name configured for this server. Order is preserved across
    /// edits/saves so the UI (tab header, account dropdown, slot filter)
    /// stays stable.
    /// </summary>
    public ObservableCollection<SlotProfile> Slots { get; } = new();

    /// <summary>"host:port" with no surrounding/inner whitespace, for compact display.</summary>
    public string HostPort => $"{Host}:{Port}";

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(HostPort));

    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(HostPort));
}
