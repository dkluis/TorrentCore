using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Engine;

public sealed class TorrentManagerStopCoordinator(
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    RuntimeOperationDurationDiagnostics durationDiagnostics)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<Guid, Lazy<Task<TorrentManagerStopResult>>> _stops = new();

    public bool TryTakeCompletedOrSchedule(
        Guid torrentId,
        string torrentName,
        Func<CancellationToken, Task> stopOperation,
        CancellationToken cancellationToken,
        out TorrentManagerStopResult? result)
    {
        while (true)
        {
            var stop = _stops.GetOrAdd(
                torrentId,
                _ => new Lazy<Task<TorrentManagerStopResult>>(
                    () => Task.Run(
                        async () => await RunStopAsync(
                            torrentId, torrentName, stopOperation, cancellationToken
                        ),
                        CancellationToken.None
                    ),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );
            var task = stop.Value;
            if (!task.IsCompleted)
            {
                result = null;
                return false;
            }

            var completedResult = task.GetAwaiter().GetResult();
            if (completedResult.Succeeded)
            {
                _stops.TryRemove(new KeyValuePair<Guid, Lazy<Task<TorrentManagerStopResult>>>(torrentId, stop));
                result = completedResult;
                return true;
            }

            if (DateTimeOffset.UtcNow - completedResult.CompletedAtUtc < RetryDelay)
            {
                result = completedResult;
                return false;
            }

            _stops.TryRemove(new KeyValuePair<Guid, Lazy<Task<TorrentManagerStopResult>>>(torrentId, stop));
        }
    }

    public void Remove(Guid torrentId)
    {
        _stops.TryRemove(torrentId, out _);
    }

    public async Task WaitForPendingAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        if (!_stops.TryGetValue(torrentId, out var stop) || !stop.IsValueCreated)
        {
            return;
        }

        await stop.Value.WaitAsync(cancellationToken);
        _stops.TryRemove(new KeyValuePair<Guid, Lazy<Task<TorrentManagerStopResult>>>(torrentId, stop));
    }

    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        var tasks = _stops.Values.Where(stop => stop.IsValueCreated).Select(stop => stop.Value).ToArray();
        if (tasks.Length == 0)
        {
            return true;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    private async Task<TorrentManagerStopResult> RunStopAsync(
        Guid torrentId,
        string torrentName,
        Func<CancellationToken, Task> stopOperation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "succeeded";
        string? error = null;
        try
        {
            await stopOperation(cancellationToken);
            return new TorrentManagerStopResult(true, null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "canceled";
            error = "The background manager stop was canceled during service shutdown.";
            return new TorrentManagerStopResult(false, error, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            outcome = "failed";
            error = exception.Message;
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.completion.manager_stop_failed",
                    Message = $"Background completion stop failed for torrent '{torrentName}'.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            TorrentId = torrentId,
                            TorrentName = torrentName,
                            Error = exception.Message,
                            RetryDelaySeconds = RetryDelay.TotalSeconds,
                        }
                    ),
                },
                CancellationToken.None
            );
            return new TorrentManagerStopResult(false, error, DateTimeOffset.UtcNow);
        }
        finally
        {
            stopwatch.Stop();
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = outcome == "succeeded" ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.completion.manager_stop_completed",
                    Message = $"Background completion stop for torrent '{torrentName}' {outcome} in {stopwatch.Elapsed.TotalMilliseconds:F0} ms.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            TorrentId = torrentId,
                            TorrentName = torrentName,
                            DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                            Outcome = outcome,
                            Error = error,
                        }
                    ),
                },
                CancellationToken.None
            );
            await durationDiagnostics.RecordIfSlowAsync(
                "monotorrent",
                "completion_manager_stop",
                stopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold,
                outcome,
                torrentId,
                new { TorrentName = torrentName, Error = error }
            );
        }
    }
}

public sealed record TorrentManagerStopResult(bool Succeeded, string? Error, DateTimeOffset CompletedAtUtc);
