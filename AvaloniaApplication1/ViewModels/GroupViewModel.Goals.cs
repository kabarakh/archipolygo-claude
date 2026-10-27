using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Archipolygo.Models;

namespace Archipolygo.ViewModels;

/// <summary>Which slots in this room have reached their goal.</summary>
public partial class GroupViewModel
{
    // Session callbacks look statuses up off the UI thread.
    private readonly ConcurrentDictionary<int, RoomSlotStatus> _roomSlotStatuses = new();

    private string? _goalSeed;

    /// <summary>Raised on the UI thread after <see cref="ApplyGoals"/> changed at least one status.</summary>
    public event Action? GoalStatesChanged;

    public RoomSlotStatus GetRoomSlotStatus(int slotNumber) =>
        _roomSlotStatuses.GetOrAdd(slotNumber, number => new RoomSlotStatus(number));

    public IReadOnlyCollection<int> GoaledSlotNumbers =>
        _roomSlotStatuses.Values.Where(status => status.HasGoal).Select(status => status.SlotNumber).ToList();

    /// <summary>
    /// Only ever sets goals, since the server never undoes one; a late status read thus can't clear a goal
    /// a live message already reported. A different seed means a new room on the same host and port.
    /// </summary>
    public void ApplyGoals(string? seed, IEnumerable<int> goaledSlotNumbers)
    {
        var changed = false;

        if (seed is not null && !string.Equals(seed, _goalSeed, StringComparison.Ordinal))
        {
            if (_goalSeed is not null)
            {
                foreach (var status in _roomSlotStatuses.Values.Where(status => status.HasGoal))
                {
                    status.HasGoal = false;
                    changed = true;
                }
            }

            _goalSeed = seed;
        }

        foreach (var slotNumber in goaledSlotNumbers)
        {
            var status = GetRoomSlotStatus(slotNumber);
            if (!status.HasGoal)
            {
                status.HasGoal = true;
                changed = true;
            }
        }

        if (changed)
        {
            OnGoalStatesChanged();
        }
    }

    private void OnGoalStatesChanged()
    {
        if (HideGoaledEvents)
        {
            OnPropertyChanged(nameof(VisibleEvents));
        }

        if (HideGoaledHints)
        {
            OnPropertyChanged(nameof(VisibleHints));
        }

        GoalStatesChanged?.Invoke();
    }
}
