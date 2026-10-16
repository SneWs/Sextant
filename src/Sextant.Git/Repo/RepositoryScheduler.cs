namespace Sextant.Git.Repo;

public sealed class RepositoryScheduler : IDisposable
{
    private readonly object _gate = new();
    // Status, history, ref queries, and the file list start together.
    private readonly SemaphoreSlim _readSlots = new(4, 4);
    private readonly SemaphoreSlim _write = new(1, 1);
    private CancellationTokenSource _epoch = new();
    private int _activeReads;
    private int _writeHolders;
    private TaskCompletionSource<bool> _readsDone = Completed();
    private TaskCompletionSource<bool> _noWriters = Completed();
    private bool _disposed;

    public async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task? wait;
            CancellationToken epoch;
            TaskCompletionSource<bool> done;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_writeHolders > 0)
                {
                    wait = _noWriters.Task;
                    epoch = default;
                    done = _readsDone;
                }
                else
                {
                    wait = null;
                    if (++_activeReads == 1)
                        _readsDone = NewSource();
                    done = _readsDone;
                    epoch = _epoch.Token;
                }
            }

            // A writer can finish in the gap after this read decided to wait. Loop and
            // look again. Falling through would run the read without counting it, and
            // the next write would wait on a signal that never comes.
            if (wait is not null)
            {
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, epoch);
            var acquired = false;
            try
            {
                await _readSlots.WaitAsync(linked.Token).ConfigureAwait(false);
                acquired = true;
                return await work(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (acquired)
                        _readSlots.Release();
                }
                finally
                {
                    lock (_gate)
                    {
                        if (--_activeReads == 0)
                            done.TrySetResult(true);
                    }
                }
            }
        }
    }

    public async Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        Task idle;
        CancellationTokenSource epoch;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writeHolders++;
            if (_writeHolders == 1)
                _noWriters = NewSource();
            epoch = _epoch;
            idle = _activeReads == 0 ? Task.CompletedTask : _readsDone.Task;
        }

        // Cancel outside the lock. A read unwinds on this stack and needs the lock to
        // report that it has exited.
        epoch.Cancel();

        var acquired = false;
        try
        {
            await idle.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _write.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await work(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
                _write.Release();
            lock (_gate)
            {
                _writeHolders--;
                if (_writeHolders == 0)
                {
                    _epoch = new CancellationTokenSource();
                    _noWriters.TrySetResult(true);
                }
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource epoch;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            epoch = _epoch;
        }

        epoch.Cancel();
        _readSlots.Dispose();
        _write.Dispose();
    }

    private static TaskCompletionSource<bool> Completed()
    {
        var source = NewSource();
        source.SetResult(true);
        return source;
    }

    private static TaskCompletionSource<bool> NewSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
