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

    /// <summary>
    /// This slot's alias in the room, learned "for free" from the player
    /// roster (<c>PlayerInfo.Alias</c>) whenever <see cref="Services.ConnectionManager"/>
    /// reads it while the leader is connected - see
    /// <see cref="Services.ConnectionManager"/>'s <c>BuildSlotRoster</c>. Null
    /// until first learned (a brand-new slot, or one that's never shared a
    /// room with a connected leader yet). Not sent anywhere - purely
    /// display, see <see cref="DisplayName"/>.
    /// </summary>
    [ObservableProperty]
    private string? _alias;

    /// <summary>
    /// "SlotName (Alias)" - the same convention Archipelago's own chat log
    /// already uses for a player mention (e.g. "KabaDone (KabaHarkinian) has
    /// stopped tracking the game."), applied everywhere this app displays a
    /// configured slot by itself rather than quoting a chat line verbatim
    /// (dropdowns, the slot-management list, ...) - those chat lines already
    /// come pre-formatted this way from the server/library itself and need
    /// no separate handling. Just <see cref="SlotName"/> alone if no alias is
    /// known yet, or if it's identical to the slot name (the common case:
    /// most players never set a different one).
    /// </summary>
    public string DisplayName
    {
        get
        {
            var alias = EffectiveAlias;
            return string.IsNullOrEmpty(alias) || string.Equals(alias, SlotName, StringComparison.Ordinal)
                ? SlotName
                : $"{SlotName} ({alias})";
        }
    }

    /// <summary>
    /// <see cref="Alias"/>, but with a redundant trailing "(SlotName)"
    /// stripped first - observed in practice for grouped/linked slots, where
    /// <c>PlayerInfo.Alias</c> itself already comes back as e.g. "KabaDone
    /// (KabaHK)" for a slot named "KabaHK", not just "KabaDone". Without this,
    /// <see cref="DisplayName"/> would double the slot name up as "KabaHK
    /// (KabaDone (KabaHK))" instead of the intended "KabaHK (KabaDone)".
    /// </summary>
    private string? EffectiveAlias
    {
        get
        {
            var alias = Alias;
            if (string.IsNullOrEmpty(alias))
            {
                return alias;
            }

            var redundantSuffix = $" ({SlotName})";
            if (alias.EndsWith(redundantSuffix, StringComparison.OrdinalIgnoreCase))
            {
                alias = alias[..^redundantSuffix.Length];
            }

            return alias;
        }
    }

    partial void OnAliasChanged(string? value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnSlotNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    /// <summary>
    /// This slot's own "X of Y locations checked" progress (Tier 1 of
    /// Fortschrittsanzeigen.md) - populated from <c>IArchipelagoSession.Locations</c>
    /// after every successful login for this slot (leader or a brief
    /// catch-up dip alike, see <see cref="Services.ConnectionManager"/>), and
    /// kept live afterward only for the leader, via <c>CheckedLocationsUpdated</c>.
    /// Null means "never successfully synced" (a brand-new slot, or one that
    /// has never connected) - the UI shows no progress bar rather than a
    /// misleading 0/0 in that case. Persisted to <c>groups.json</c> like
    /// every other <see cref="SlotProfile"/> field; no migration needed, since
    /// <c>System.Text.Json</c> just leaves these null when reading an older
    /// file that doesn't have them.
    /// </summary>
    [ObservableProperty]
    private int? _locationsChecked;

    [ObservableProperty]
    private int? _locationsTotal;

    /// <summary>Whether this slot has ever synced a location count - drives whether a progress bar is shown for it at all.</summary>
    public bool HasLocationProgress => LocationsTotal is not null;

    /// <summary><see cref="LocationsChecked"/> as a plain <c>double</c> (defaulting to 0), for binding directly to a <c>ProgressBar.Value</c> without a converter.</summary>
    public double LocationsCheckedValue => LocationsChecked ?? 0;

    /// <summary><see cref="LocationsTotal"/> as a plain <c>double</c> (defaulting to 0), for binding directly to a <c>ProgressBar.Maximum</c> without a converter.</summary>
    public double LocationsTotalValue => LocationsTotal ?? 0;

    partial void OnLocationsCheckedChanged(int? value) => OnPropertyChanged(nameof(LocationsCheckedValue));

    partial void OnLocationsTotalChanged(int? value)
    {
        OnPropertyChanged(nameof(LocationsTotalValue));
        OnPropertyChanged(nameof(HasLocationProgress));
    }
}
