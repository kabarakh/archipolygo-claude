using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Archipolygo.Services;

/// <summary>
/// One connect handshake at a time across every server. Interactive connects (leaders, Hint picker,
/// roster probes) go before queued catch-ups. See Umsetzungsplan.md, "Globale Connect-Sperre und Catch-up-Queue".
/// </summary>
internal sealed class ConnectGate
{
    private readonly object _sync = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _interactiveWaiters = new();
    private readonly LinkedList<TaskCompletionSource<bool>> _backgroundWaiters = new();
    private readonly TimeSpan _maxHold;
    private bool _held;

    /// <param name="maxHold">A hung handshake gives the gate up after this, so it can't block every other server forever.</param>
    public ConnectGate(TimeSpan maxHold)
    {
        _maxHold = maxHold;
    }

    /// <summary>Null if <paramref name="cancellationToken"/> fired while waiting; dispose the lease to release.</summary>
    public async Task<IDisposable?> EnterAsync(bool isBackground, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        TaskCompletionSource<bool> waiter;
        LinkedListNode<TaskCompletionSource<bool>> node;
        lock (_sync)
        {
            if (!_held)
            {
                _held = true;
                return CreateLease();
            }

            waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            node = (isBackground ? _backgroundWaiters : _interactiveWaiters).AddLast(waiter);
        }

        await using (cancellationToken.Register(() => AbandonWaiter(node)))
        {
            // True = ownership was handed over by Release, which keeps _held set.
            return await waiter.Task ? CreateLease() : null;
        }
    }

    private void AbandonWaiter(LinkedListNode<TaskCompletionSource<bool>> node)
    {
        lock (_sync)
        {
            if (node.List is null)
            {
                return; // already handed the gate
            }

            node.List.Remove(node);
        }

        node.Value.TrySetResult(false);
    }

    private IDisposable CreateLease()
    {
        var lease = new Lease(this);
        _ = ReleaseAfterMaxHoldAsync(lease);
        return lease;
    }

    private async Task ReleaseAfterMaxHoldAsync(Lease lease)
    {
        try
        {
            await Task.Delay(_maxHold, lease.DisposedToken);
            lease.Dispose();
        }
        catch (OperationCanceledException)
        {
            // Released normally.
        }
    }

    private void Release()
    {
        TaskCompletionSource<bool>? next;
        lock (_sync)
        {
            var queue = _interactiveWaiters.Count > 0 ? _interactiveWaiters : _backgroundWaiters;
            next = queue.First?.Value;
            if (next is null)
            {
                _held = false;
                return;
            }

            queue.RemoveFirst();
        }

        next.TrySetResult(true);
    }

    private sealed class Lease(ConnectGate gate) : IDisposable
    {
        private readonly CancellationTokenSource _disposed = new();
        private int _released;

        public CancellationToken DisposedToken => _disposed.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _disposed.Cancel();
            gate.Release();
        }
    }
}
