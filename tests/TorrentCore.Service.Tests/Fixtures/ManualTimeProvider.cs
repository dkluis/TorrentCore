namespace TorrentCore.Service.Tests.Fixtures;

internal sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = initialUtcNow;
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerDuration(dueTime, nameof(dueTime));
        ValidateTimerDuration(period, nameof(period));

        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), "Elapsed time cannot be negative.");
        }

        List<ScheduledCallback> callbacks;
        lock (_gate)
        {
            _utcNow += elapsed;
            _timestamp = checked(_timestamp + elapsed.Ticks);
            callbacks = _timers
                .SelectMany(timer => timer.TakeDueCallbacks(_utcNow))
                .OrderBy(callback => callback.DueAtUtc)
                .ThenBy(callback => callback.Sequence)
                .ToList();
        }

        foreach (var callback in callbacks)
        {
            callback.Callback(callback.State);
        }
    }

    private static void ValidateTimerDuration(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private static long _nextSequence;
        private DateTimeOffset? _nextDueAtUtc = ResolveNextDue(owner._utcNow, dueTime);
        private TimeSpan _period = NormalizePeriod(period);
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimerDuration(dueTime, nameof(dueTime));
            ValidateTimerDuration(period, nameof(period));

            lock (owner._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _nextDueAtUtc = ResolveNextDue(owner._utcNow, dueTime);
                _period = NormalizePeriod(period);
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _nextDueAtUtc = null;
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public IEnumerable<ScheduledCallback> TakeDueCallbacks(DateTimeOffset utcNow)
        {
            while (!_disposed && _nextDueAtUtc is { } dueAtUtc && dueAtUtc <= utcNow)
            {
                yield return new ScheduledCallback(
                    dueAtUtc,
                    Interlocked.Increment(ref _nextSequence),
                    callback,
                    state
                );

                _nextDueAtUtc = _period == Timeout.InfiniteTimeSpan
                    ? null
                    : dueAtUtc + _period;
            }
        }

        private static DateTimeOffset? ResolveNextDue(DateTimeOffset utcNow, TimeSpan dueTime)
            => dueTime == Timeout.InfiniteTimeSpan ? null : utcNow + dueTime;

        private static TimeSpan NormalizePeriod(TimeSpan period)
            => period == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : period;
    }

    private sealed record ScheduledCallback(
        DateTimeOffset DueAtUtc,
        long Sequence,
        TimerCallback Callback,
        object? State);
}
