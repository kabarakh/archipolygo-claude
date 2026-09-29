using System;
using System.Collections.Generic;

namespace Archipolygo.ViewModels;

/// <summary>
/// One player of the room in the Admin view's player list (Admin-Funktionen.md
/// in the feature-plan archive): name/game from the live room roster, status/
/// checks/inactivity from the room's webhost tracker when one is configured.
/// </summary>
public sealed class AdminPlayerRowViewModel : ViewModelBase
{
    /// <summary>Decided 2026-09-29: highlighted yellow from 3 days without a new check, red from 5 days.</summary>
    public static readonly TimeSpan InactiveWarningThreshold = TimeSpan.FromDays(3);
    public static readonly TimeSpan InactiveCriticalThreshold = TimeSpan.FromDays(5);

    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<long> _checkedLocationIds;

    public AdminPlayerRowViewModel(Func<DateTimeOffset> clock, IEnumerable<long>? checkedLocationIds = null)
    {
        _clock = clock;
        _checkedLocationIds = new HashSet<long>(checkedLocationIds ?? Array.Empty<long>());
    }

    public required int Team { get; init; }
    public required int Slot { get; init; }

    /// <summary>The slot name - what every admin command addresses the player by.</summary>
    public required string Name { get; init; }

    public string? Alias { get; init; }
    public required string Game { get; init; }

    /// <summary>One of this app's own configured slots on this server.</summary>
    public bool IsOwnSlot { get; init; }

    /// <summary>False without a tracker id (or before the tracker answered) - only name/game are known then.</summary>
    public bool HasTrackerData { get; init; }

    public int ChecksDone { get; private set; }
    public int? ChecksTotal { get; init; }
    public DateTimeOffset? LastActivity { get; init; }

    /// <summary>Archipelago ClientStatus as the tracker reports it (0/5/10/20/30).</summary>
    public int? ClientStatus { get; init; }

    public int InitialChecksDone { init => ChecksDone = value; }

    /// <summary>Same "SlotName (Alias)" rule as everywhere else - see <see cref="Models.SlotProfile.FormatDisplayName"/>.</summary>
    public string DisplayName => Models.SlotProfile.FormatDisplayName(Name, Alias);

    public string ChecksText => ChecksTotal is not null ? $"{ChecksDone}/{ChecksTotal} checks" : $"{ChecksDone} checks";

    /// <summary>Same wording as the webhost tracker's own "Status" column.</summary>
    public string StatusText => ClientStatus switch
    {
        30 => "Goal",
        20 => "Playing",
        10 => "Ready",
        5 => "Connected",
        _ => "Disconnected",
    };

    public bool IsGoal => ClientStatus == 30;

    public TimeSpan? InactiveFor => LastActivity is null ? null : _clock() - LastActivity.Value;

    public string InactiveText
    {
        get
        {
            if (InactiveFor is not { } span)
            {
                return "never active";
            }

            if (span < TimeSpan.Zero)
            {
                span = TimeSpan.Zero;
            }

            if (span.TotalDays >= 1)
            {
                return $"inactive {(int)span.TotalDays}d {span.Hours}h";
            }

            return span.TotalHours >= 1
                ? $"inactive {(int)span.TotalHours}h {span.Minutes:00}m"
                : $"inactive {span.Minutes}m {span.Seconds:00}s";
        }
    }

    public bool IsInactiveWarning => InactiveFor is { } span && span >= InactiveWarningThreshold && span < InactiveCriticalThreshold;

    public bool IsInactiveCritical => InactiveFor is { } span && span >= InactiveCriticalThreshold;

    /// <summary>"Longest inactive first" sort key - a player who was never active counts as the most inactive.</summary>
    public TimeSpan InactiveSortKey => InactiveFor ?? TimeSpan.MaxValue;

    public bool IsLocationChecked(long locationId) => _checkedLocationIds.Contains(locationId);

    /// <summary>
    /// Called right after a successful "/send_location", so the location
    /// disappears from the dialog's list at once instead of only after the
    /// next tracker refresh (up to 60 s later).
    /// </summary>
    public void MarkLocationChecked(long locationId)
    {
        if (_checkedLocationIds.Add(locationId))
        {
            ChecksDone++;
            OnPropertyChanged(nameof(ChecksDone));
            OnPropertyChanged(nameof(ChecksText));
        }
    }

    /// <summary>Re-evaluates the live-counting inactivity text/highlight - called once a second by <see cref="AdminPanelViewModel"/>.</summary>
    public void RefreshInactivity()
    {
        OnPropertyChanged(nameof(InactiveText));
        OnPropertyChanged(nameof(IsInactiveWarning));
        OnPropertyChanged(nameof(IsInactiveCritical));
    }
}
