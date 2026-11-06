using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Archipolygo.ViewModels;
using Avalonia.Threading;

namespace Archipolygo.Services;

/// <summary>
/// The one path for list data (events, hints, received items, attention reports) onto the UI thread. Applies the
/// changes in time slices below input and render priority, so a burst of thousands of lines streams in visibly
/// while the app stays responsive, and a quiet room still shows each line at once.
/// See Umsetzungsplan.md, "UI-Updates in Zeitscheiben".
/// </summary>
public interface IUiUpdateQueue
{
    /// <summary>Thread-safe. Changes apply in enqueue order, across all groups and lists.</summary>
    void Enqueue(GroupViewModel group, Action apply);
}

/// <inheritdoc cref="IUiUpdateQueue"/>
public sealed class UiUpdateQueue : IUiUpdateQueue
{
    /// <summary>App-wide: a second queue would break the cross-list order. Services default to it.</summary>
    public static UiUpdateQueue Shared { get; } = new();

    /// <summary>Leaves the rest of a 60 Hz frame for input, layout and render.</summary>
    internal static readonly TimeSpan SliceBudget = TimeSpan.FromMilliseconds(8);

    private readonly ConcurrentQueue<(GroupViewModel Group, Action Apply)> _pending = new();
    private int _flushScheduled;

    public void Enqueue(GroupViewModel group, Action apply)
    {
        _pending.Enqueue((group, apply));
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(FlushSlice, DispatcherPriority.Background);
        }
    }

    private void FlushSlice()
    {
        var stopwatch = Stopwatch.StartNew();
        var batchedGroups = new List<GroupViewModel>();

        try
        {
            // At least one change per slice, even if a single change outlasts the budget.
            do
            {
                if (!_pending.TryDequeue(out var update))
                {
                    break;
                }

                if (!batchedGroups.Contains(update.Group))
                {
                    update.Group.BeginUpdateBatch();
                    batchedGroups.Add(update.Group);
                }

                update.Apply();
            }
            while (stopwatch.Elapsed < SliceBudget);
        }
        finally
        {
            foreach (var group in batchedGroups)
            {
                group.EndUpdateBatch();
            }

            // Reset before the emptiness check: an Enqueue racing in between then either sees the reset flag or is seen here.
            Volatile.Write(ref _flushScheduled, 0);
            if (!_pending.IsEmpty)
            {
                ScheduleFlush();
            }
        }
    }
}
