using System;
using System.Collections.Generic;

namespace Archipolygo.ViewModels;

/// <summary>Admin player-list row: name/game from the live roster, status/checks/inactivity from the webhost tracker.</summary>
public sealed class AdminPlayerRowViewModel : ViewModelBase
{
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

    /// <summary>The slot name, which admin commands address the player by.</summary>
    public required string Name { get; init; }

    public string? Alias { get; init; }
    public required string Game { get; init; }

    public bool IsOwnSlot { get; init; }

    public bool HasTrackerData { get; init; }

    public int ChecksDone { get; private set; }
    public int? ChecksTotal { get; init; }
    public DateTimeOffset? LastActivity { get; init; }

    /// <summary>Archipelago ClientStatus (0/5/10/20/30).</summary>
    public int? ClientStatus { get; init; }

    public int InitialChecksDone { init => ChecksDone = value; }

    public string DisplayName => Models.SlotProfile.FormatDisplayName(Name, Alias);

    public string ChecksText => ChecksTotal is not null ? $"{ChecksDone}/{ChecksTotal} checks" : $"{ChecksDone} checks";

    /// <summary>Same wording as the webhost tracker's "Status" column.</summary>
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

    /// <summary>A player who was never active counts as the most inactive.</summary>
    public TimeSpan InactiveSortKey => InactiveFor ?? TimeSpan.MaxValue;

    public bool IsLocationChecked(long locationId) => _checkedLocationIds.Contains(locationId);

    /// <summary>After "/send_location", so the location vanishes now rather than at the next tracker refresh (up to 60 s).</summary>
    public void MarkLocationChecked(long locationId)
    {
        if (_checkedLocationIds.Add(locationId))
        {
            ChecksDone++;
            OnPropertyChanged(nameof(ChecksDone));
            OnPropertyChanged(nameof(ChecksText));
        }
    }

    public void RefreshInactivity()
    {
        OnPropertyChanged(nameof(InactiveText));
        OnPropertyChanged(nameof(IsInactiveWarning));
        OnPropertyChanged(nameof(IsInactiveCritical));
    }
}
