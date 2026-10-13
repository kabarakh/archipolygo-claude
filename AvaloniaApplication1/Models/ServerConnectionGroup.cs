using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Archipolygo.Models;

/// <summary>
/// One Archipelago server (Host, Port, shared Password) and every slot configured on it.
/// At most one slot per group is the connected "leader" at a time - see <see cref="Services.ConnectionManager"/>.
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

    /// <summary>Room password shared by every slot; in-memory only, never persisted (<see cref="SlotProfile.RequiresPassword"/> is).</summary>
    [ObservableProperty]
    [property: JsonIgnore]
    private string _password = string.Empty;

    /// <summary>
    /// Connect the leader at startup and auto-reconnect after a drop. Set by any successful leader connect,
    /// cleared by an explicit disconnect (see ConnectionManager).
    /// </summary>
    [ObservableProperty]
    private bool _autoConnect;

    /// <summary>The slot to become leader at startup; updated to whichever slot last connected as leader. Null falls back to the first slot.</summary>
    [ObservableProperty]
    private Guid? _preferredLeaderSlotId;

    /// <summary>A muted server never counts or blinks, overriding every per-category setting.</summary>
    [ObservableProperty]
    private bool _notificationsMuted;

    /// <summary>"I'm the admin": only shows the Admin view; the admin password is asked for there and never persisted.</summary>
    [ObservableProperty]
    private bool _isAdmin;

    /// <summary>
    /// Get-only, so PersistenceService needs JsonObjectCreationHandling.Populate or this deserializes empty.
    /// Order is preserved so the UI stays stable.
    /// </summary>
    public ObservableCollection<SlotProfile> Slots { get; } = new();

    /// <summary>
    /// A <see cref="ServerColorPalette.Colors"/> name (or a manually picked color), assigned once and never reassigned.
    /// Empty only briefly between creating/deserializing a group and that assignment.
    /// </summary>
    [ObservableProperty]
    private string _color = string.Empty;

    /// <summary>
    /// Exactly what the user typed into the tracker field (tracker id, room URL or tracker URL), kept so the editor
    /// can redisplay it; <see cref="TrackerId"/> holds the resolved id. Null/empty disables whole-room progress.
    /// </summary>
    [ObservableProperty]
    private string? _trackerReferenceInput;

    /// <summary>The resolved tracker id that gets polled; null means no whole-room progress for this group.</summary>
    [ObservableProperty]
    private string? _trackerId;

    public string HostPort => $"{Host}:{Port}";

    /// <summary>
    /// <see cref="PreferredLeaderSlotId"/> if still configured, else the first slot. A method rather than a
    /// property so it never ends up in groups.json.
    /// </summary>
    public SlotProfile? ResolveStartupLeader() =>
        Slots.FirstOrDefault(s => s.Id == PreferredLeaderSlotId) ?? Slots.FirstOrDefault();

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(HostPort));

    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(HostPort));
}
