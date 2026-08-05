using System.Diagnostics;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Engine;

public sealed class TorrentMetadataResetCoordinator
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCircuitOpenDuration = TimeSpan.FromMinutes(5);

    private readonly IActivityLogService _activityLogService;
    private readonly ServiceInstanceContext _serviceInstanceContext;
    private readonly RuntimeOperationDurationDiagnostics _durationDiagnostics;
    private readonly TimeSpan _circuitOpenDuration;
    private readonly object _stateGate = new();
    private readonly Dictionary<Guid, TorrentMetadataResetResult> _completedResults = new();
    private readonly Dictionary<Guid, FailedResetCooldown> _failedResetCooldowns = new();
    private readonly HashSet<(Guid TorrentId, string Reason)> _loggedSuppressions = [];

    private ActiveReset? _activeReset;
    private MetadataResetCircuitState _circuitState = MetadataResetCircuitState.Closed;
    private DateTimeOffset? _circuitOpenUntilUtc;

    public TorrentMetadataResetCoordinator(
        IActivityLogService activityLogService,
        ServiceInstanceContext serviceInstanceContext,
        RuntimeOperationDurationDiagnostics durationDiagnostics)
        : this(
            activityLogService,
            serviceInstanceContext,
            durationDiagnostics,
            DefaultCircuitOpenDuration)
    {
    }

    internal TorrentMetadataResetCoordinator(
        IActivityLogService activityLogService,
        ServiceInstanceContext serviceInstanceContext,
        RuntimeOperationDurationDiagnostics durationDiagnostics,
        TimeSpan circuitOpenDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(circuitOpenDuration, TimeSpan.Zero);
        _activityLogService = activityLogService;
        _serviceInstanceContext = serviceInstanceContext;
        _durationDiagnostics = durationDiagnostics;
        _circuitOpenDuration = circuitOpenDuration;
    }

    public bool IsRunning(Guid torrentId)
    {
        lock (_stateGate)
        {
            return _activeReset?.TorrentId == torrentId;
        }
    }

    public bool TryTakeCompleted(Guid torrentId, out TorrentMetadataResetResult? result)
    {
        lock (_stateGate)
        {
            if (!_completedResults.Remove(torrentId, out result))
            {
                result = null;
                return false;
            }

            return true;
        }
    }

    public bool TryTakeCompletedOrSchedule(
        Guid torrentId,
        string torrentName,
        TimeSpan stuckThreshold,
        Func<CancellationToken, Task> resetOperation,
        CancellationToken cancellationToken,
        out TorrentMetadataResetResult? result)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stuckThreshold, TimeSpan.Zero);

        ActiveReset? scheduledReset = null;
        string? suppressionReason = null;
        DateTimeOffset? retryAtUtc = null;
        var now = DateTimeOffset.UtcNow;

        lock (_stateGate)
        {
            if (_completedResults.Remove(torrentId, out result))
            {
                return true;
            }

            if (_activeReset is not null)
            {
                suppressionReason = _activeReset.TorrentId == torrentId ? "duplicate_active_reset" : "active_reset";
                retryAtUtc = _circuitOpenUntilUtc;
            }
            else if (_failedResetCooldowns.TryGetValue(torrentId, out var failedCooldown) &&
                     failedCooldown.RetryAtUtc > now)
            {
                result = failedCooldown.Result;
                suppressionReason = "retry_cooldown";
                retryAtUtc = failedCooldown.RetryAtUtc;
            }
            else
            {
                _failedResetCooldowns.Remove(torrentId);
                var isHalfOpenProbe = false;
                if (_circuitState == MetadataResetCircuitState.Open)
                {
                    if (_circuitOpenUntilUtc is { } openUntilUtc && openUntilUtc > now)
                    {
                        suppressionReason = "circuit_open";
                        retryAtUtc = openUntilUtc;
                    }
                    else
                    {
                        _circuitState = MetadataResetCircuitState.HalfOpen;
                        isHalfOpenProbe = true;
                    }
                }

                if (suppressionReason is null)
                {
                    scheduledReset = new ActiveReset(
                        torrentId,
                        torrentName,
                        stuckThreshold,
                        isHalfOpenProbe,
                        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                    _activeReset = scheduledReset;
                    _loggedSuppressions.Clear();
                }
            }
        }

        if (suppressionReason is not null)
        {
            WriteSuppressionOnce(torrentId, torrentName, suppressionReason, retryAtUtc);
            return result is not null;
        }

        result = null;
        if (scheduledReset is null)
        {
            return false;
        }

        if (scheduledReset.IsHalfOpenProbe)
        {
            _ = WriteCircuitEventAsync(
                "runtime.metadata.reset_half_open",
                ActivityLogLevel.Warning,
                $"Automatic metadata reset circuit entered half-open state for torrent '{torrentName}'.",
                scheduledReset,
                new { Probe = true });
        }

        _ = Task.Run(
            async () => await RunCoordinatedResetAsync(
                scheduledReset,
                resetOperation,
                cancellationToken),
            CancellationToken.None);
        return false;
    }

    public void Remove(Guid torrentId)
    {
        lock (_stateGate)
        {
            _completedResults.Remove(torrentId);
            _failedResetCooldowns.Remove(torrentId);
            _loggedSuppressions.RemoveWhere(item => item.TorrentId == torrentId);
        }
    }

    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        Task? activeTask;
        lock (_stateGate)
        {
            activeTask = _activeReset?.Completion.Task;
        }

        if (activeTask is null)
        {
            return true;
        }

        try
        {
            await activeTask.WaitAsync(timeout);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    private async Task RunCoordinatedResetAsync(
        ActiveReset activeReset,
        Func<CancellationToken, Task> resetOperation,
        CancellationToken cancellationToken)
    {
        var resetTask = RunResetAsync(
            activeReset.TorrentId,
            activeReset.TorrentName,
            resetOperation,
            cancellationToken);
        var watchdogTask = Task.Delay(activeReset.StuckThreshold, cancellationToken);
        var timedOut = false;

        try
        {
            var firstCompleted = await Task.WhenAny(resetTask, watchdogTask);
            if (firstCompleted == watchdogTask &&
                !watchdogTask.IsCanceled &&
                !resetTask.IsCompleted)
            {
                timedOut = true;
                var openUntilUtc = DateTimeOffset.UtcNow.Add(_circuitOpenDuration);
                lock (_stateGate)
                {
                    if (ReferenceEquals(_activeReset, activeReset))
                    {
                        activeReset.TimedOut = true;
                        _circuitState = MetadataResetCircuitState.Open;
                        _circuitOpenUntilUtc = openUntilUtc;
                        _loggedSuppressions.Clear();
                    }
                }

                await WriteCircuitEventAsync(
                    "runtime.metadata.reset_timed_out",
                    ActivityLogLevel.Error,
                    $"Automatic metadata reset for torrent '{activeReset.TorrentName}' exceeded its stuck threshold and remains quarantined.",
                    activeReset,
                    new
                    {
                        StuckThresholdSeconds = activeReset.StuckThreshold.TotalSeconds,
                        UnderlyingOperationStillRunning = true,
                    });
                await WriteCircuitEventAsync(
                    "runtime.metadata.reset_circuit_opened",
                    ActivityLogLevel.Error,
                    "Automatic metadata reset circuit opened after a stuck operation.",
                    activeReset,
                    new
                    {
                        OpenUntilUtc = openUntilUtc,
                        CircuitOpenDurationSeconds = _circuitOpenDuration.TotalSeconds,
                        Reason = "stuck_reset",
                    });
            }

            var result = await resetTask;
            if (timedOut)
            {
                await WriteCircuitEventAsync(
                    "runtime.metadata.reset_late_completion",
                    result.Succeeded ? ActivityLogLevel.Warning : ActivityLogLevel.Error,
                    $"Quarantined automatic metadata reset for torrent '{activeReset.TorrentName}' eventually completed.",
                    activeReset,
                    new
                    {
                        result.Succeeded,
                        result.Error,
                        QuarantineReleased = true,
                    });
            }

            string? followUpEvent = null;
            DateTimeOffset? reopenedUntilUtc = null;
            lock (_stateGate)
            {
                if (ReferenceEquals(_activeReset, activeReset))
                {
                    _activeReset = null;
                    _completedResults[activeReset.TorrentId] = result;
                    _loggedSuppressions.Clear();

                    if (!result.Succeeded)
                    {
                        _failedResetCooldowns[activeReset.TorrentId] =
                                new FailedResetCooldown(result.CompletedAtUtc.Add(RetryDelay), result);
                    }

                    if (activeReset.IsHalfOpenProbe && !timedOut)
                    {
                        if (result.Succeeded)
                        {
                            _circuitState = MetadataResetCircuitState.Closed;
                            _circuitOpenUntilUtc = null;
                            followUpEvent = "closed";
                        }
                        else
                        {
                            _circuitState = MetadataResetCircuitState.Open;
                            reopenedUntilUtc = DateTimeOffset.UtcNow.Add(_circuitOpenDuration);
                            _circuitOpenUntilUtc = reopenedUntilUtc;
                            followUpEvent = "reopened";
                        }
                    }
                }
            }

            if (followUpEvent == "closed")
            {
                await WriteCircuitEventAsync(
                    "runtime.metadata.reset_circuit_closed",
                    ActivityLogLevel.Information,
                    "Automatic metadata reset circuit closed after a successful half-open probe.",
                    activeReset,
                    new { ProbeSucceeded = true });
            }
            else if (followUpEvent == "reopened")
            {
                await WriteCircuitEventAsync(
                    "runtime.metadata.reset_circuit_opened",
                    ActivityLogLevel.Error,
                    "Automatic metadata reset circuit reopened after a failed half-open probe.",
                    activeReset,
                    new
                    {
                        OpenUntilUtc = reopenedUntilUtc,
                        CircuitOpenDurationSeconds = _circuitOpenDuration.TotalSeconds,
                        Reason = "half_open_probe_failed",
                    });
            }
        }
        finally
        {
            activeReset.Completion.TrySetResult();
        }
    }

    private void WriteSuppressionOnce(
        Guid torrentId,
        string torrentName,
        string reason,
        DateTimeOffset? retryAtUtc)
    {
        lock (_stateGate)
        {
            if (!_loggedSuppressions.Add((torrentId, reason)))
            {
                return;
            }
        }

        _ = _activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Warning,
                Category = "runtime",
                EventType = "runtime.metadata.reset_suppressed",
                Message = $"Automatic metadata reset for torrent '{torrentName}' was suppressed ({reason}).",
                TorrentId = torrentId,
                ServiceInstanceId = _serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        TorrentId = torrentId,
                        TorrentName = torrentName,
                        Reason = reason,
                        RetryAtUtc = retryAtUtc,
                    })
            },
            CancellationToken.None);
    }

    private Task WriteCircuitEventAsync(
        string eventType,
        ActivityLogLevel level,
        string message,
        ActiveReset activeReset,
        object details)
    {
        return _activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = level,
                Category = "runtime",
                EventType = eventType,
                Message = message,
                TorrentId = activeReset.TorrentId,
                ServiceInstanceId = _serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        TorrentId = activeReset.TorrentId,
                        TorrentName = activeReset.TorrentName,
                        details,
                    })
            },
            CancellationToken.None);
    }

    private async Task<TorrentMetadataResetResult> RunResetAsync(
        Guid torrentId,
        string torrentName,
        Func<CancellationToken, Task> resetOperation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "succeeded";
        string? error = null;
        try
        {
            await resetOperation(cancellationToken);
            return new TorrentMetadataResetResult(true, null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            error = "The background metadata reset was canceled during service shutdown.";
            return new TorrentMetadataResetResult(false, error, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            outcome = "failed";
            error = exception.Message;
            await _activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.metadata.reset_failed",
                    Message = $"Background metadata reset failed for torrent '{torrentName}'.",
                    TorrentId = torrentId,
                    ServiceInstanceId = _serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            TorrentId = torrentId,
                            TorrentName = torrentName,
                            Error = exception.Message,
                            RetryDelaySeconds = RetryDelay.TotalSeconds,
                        })
                },
                CancellationToken.None);
            return new TorrentMetadataResetResult(false, error, DateTimeOffset.UtcNow);
        }
        finally
        {
            stopwatch.Stop();
            await _activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = outcome == "succeeded" ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.metadata.reset_completed",
                    Message = $"Background metadata reset for torrent '{torrentName}' {outcome} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.",
                    TorrentId = torrentId,
                    ServiceInstanceId = _serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            TorrentId = torrentId,
                            TorrentName = torrentName,
                            DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                            Outcome = outcome,
                            Error = error,
                        })
                },
                CancellationToken.None);
            await _durationDiagnostics.RecordIfSlowAsync(
                "monotorrent",
                "metadata_session_reset",
                stopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold,
                outcome,
                torrentId,
                new { TorrentName = torrentName, Error = error });
        }
    }

    private sealed class ActiveReset(
        Guid torrentId,
        string torrentName,
        TimeSpan stuckThreshold,
        bool isHalfOpenProbe,
        TaskCompletionSource completion)
    {
        public Guid TorrentId { get; } = torrentId;
        public string TorrentName { get; } = torrentName;
        public TimeSpan StuckThreshold { get; } = stuckThreshold;
        public bool IsHalfOpenProbe { get; } = isHalfOpenProbe;
        public TaskCompletionSource Completion { get; } = completion;
        public bool TimedOut { get; set; }
    }

    private sealed record FailedResetCooldown(DateTimeOffset RetryAtUtc, TorrentMetadataResetResult Result);

    private enum MetadataResetCircuitState
    {
        Closed,
        Open,
        HalfOpen,
    }
}

public sealed record TorrentMetadataResetResult(bool Succeeded, string? Error, DateTimeOffset CompletedAtUtc);
