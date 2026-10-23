using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipolygo.Models;
using Archipolygo.ViewModels;

namespace Archipolygo.Services;

/// <summary>
/// Every server's pending slot catch-ups in one list, worked off one at a time in order.
/// See Umsetzungsplan.md, "Globale Connect-Sperre und Catch-up-Queue".
/// </summary>
internal sealed class CatchUpQueue
{
    private readonly object _sync = new();
    private readonly LinkedList<Entry> _pending = new();
    private readonly Func<GroupViewModel, SlotProfile, Task> _process;
    private readonly Action<int> _announce;
    private readonly Action<GroupViewModel, SlotProfile> _completed;
    private Entry? _current;
    private TaskCompletionSource? _idle;

    /// <param name="process">Must not throw.</param>
    /// <param name="announce">Called with the number of newly added slots, before any of them can complete.</param>
    /// <param name="completed">Called once per announced slot: processed or removed.</param>
    public CatchUpQueue(
        Func<GroupViewModel, SlotProfile, Task> process,
        Action<int> announce,
        Action<GroupViewModel, SlotProfile> completed)
    {
        _process = process;
        _announce = announce;
        _completed = completed;
    }

    /// <summary>Appends at the end; a slot already pending or in flight isn't added twice. Completes once all given slots are done.</summary>
    public Task EnqueueAsync(GroupViewModel group, IReadOnlyList<SlotProfile> slots)
    {
        var completions = new List<Task>();
        var startWorker = false;

        lock (_sync)
        {
            var added = 0;
            foreach (var slot in slots)
            {
                if (FindQueued(slot.Id) is { } existing)
                {
                    completions.Add(existing.Completion.Task);
                    continue;
                }

                var entry = new Entry(group, slot);
                _pending.AddLast(entry);
                completions.Add(entry.Completion.Task);
                added++;
            }

            if (added > 0)
            {
                _announce(added);
            }

            if (_idle is null && _pending.Count > 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                startWorker = true;
            }
        }

        // Runs on the caller's context, like the per-server sweep this replaced.
        if (startWorker)
        {
            _ = RunWorkerAsync();
        }

        return Task.WhenAll(completions);
    }

    /// <summary>Drops the group's pending entries (an in-flight one finishes on its own).</summary>
    public void RemoveGroup(Guid groupId)
    {
        List<Entry> removed;
        lock (_sync)
        {
            removed = _pending.Where(e => e.Group.Group.Id == groupId).ToList();
            foreach (var entry in removed)
            {
                _pending.Remove(entry);
            }
        }

        foreach (var entry in removed)
        {
            Finish(entry);
        }
    }

    /// <summary>Completes once the worker has nothing left to do.</summary>
    internal Task WhenIdleAsync()
    {
        lock (_sync)
        {
            return _idle?.Task ?? Task.CompletedTask;
        }
    }

    private Entry? FindQueued(Guid slotId) =>
        _current?.Slot.Id == slotId ? _current : _pending.FirstOrDefault(e => e.Slot.Id == slotId);

    private async Task RunWorkerAsync()
    {
        while (true)
        {
            Entry entry;
            lock (_sync)
            {
                _current = null;
                if (_pending.First is not { } first)
                {
                    _idle?.TrySetResult();
                    _idle = null;
                    return;
                }

                entry = first.Value;
                _pending.RemoveFirst();
                _current = entry;
            }

            try
            {
                await _process(entry.Group, entry.Slot);
            }
            catch
            {
                // _process logs its own failures; a stray throw must not stop the worker for every server.
            }
            finally
            {
                Finish(entry);
            }
        }
    }

    private void Finish(Entry entry)
    {
        _completed(entry.Group, entry.Slot);
        entry.Completion.TrySetResult();
    }

    private sealed class Entry(GroupViewModel group, SlotProfile slot)
    {
        public GroupViewModel Group { get; } = group;
        public SlotProfile Slot { get; } = slot;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
