namespace TorrentCore.Service.Engine;

public sealed class TorrentExecutionGate(bool initiallyOpen = true)
{
    private readonly object _gate = new();
    private int _activeLeaseCount;
    private TaskCompletionSource? _drainedSource;
    private bool _isOpen = initiallyOpen;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _isOpen;
            }
        }
    }

    public IDisposable? TryAcquire()
    {
        lock (_gate)
        {
            if (!_isOpen)
            {
                return null;
            }

            _activeLeaseCount++;
            return new ExecutionLease(this);
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Task drainedTask;
        lock (_gate)
        {
            _isOpen = false;
            if (_activeLeaseCount == 0)
            {
                return Task.CompletedTask;
            }

            _drainedSource ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drainedTask = _drainedSource.Task;
        }

        return drainedTask.WaitAsync(cancellationToken);
    }

    public void Open()
    {
        lock (_gate)
        {
            if (_isOpen)
            {
                return;
            }

            if (_activeLeaseCount != 0)
            {
                throw new InvalidOperationException(
                    "Torrent execution cannot reopen until all operations admitted before closure have completed."
                );
            }

            _drainedSource = null;
            _isOpen = true;
        }
    }

    private void Release()
    {
        TaskCompletionSource? drainedSource = null;
        lock (_gate)
        {
            if (_activeLeaseCount <= 0)
            {
                throw new InvalidOperationException("Torrent execution gate lease accounting is inconsistent.");
            }

            _activeLeaseCount--;
            if (!_isOpen && _activeLeaseCount == 0)
            {
                drainedSource = _drainedSource;
            }
        }

        drainedSource?.TrySetResult();
    }

    private sealed class ExecutionLease(TorrentExecutionGate owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
