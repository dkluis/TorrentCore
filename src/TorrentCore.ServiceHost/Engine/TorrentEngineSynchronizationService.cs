#region

using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class TorrentEngineSynchronizationService(ITorrentEngineAdapter torrentEngineAdapter,
    IOptions<TorrentCoreServiceOptions> serviceOptions, IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    RuntimeOperationDurationDiagnostics durationDiagnostics) : BackgroundService
{
    private static readonly TimeSpan DurationSummaryInterval = TimeSpan.FromMinutes(1);
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_serviceOptions.RuntimeTickIntervalMilliseconds));
        var summaryStartedAt = DateTimeOffset.UtcNow;
        var summaryDurationTicks = 0L;
        var summaryMaximumTicks = 0L;
        var summarySampleCount = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStopwatch = Stopwatch.StartNew();
            var outcome = "succeeded";
            try
            {
                await torrentEngineAdapter.SynchronizeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                outcome = "failed";
                await activityLogService.TryWriteActivityLogAsync(
                    new ActivityLogWriteRequest
                    {
                        Level             = ActivityLogLevel.Error,
                        Category          = "runtime",
                        EventType         = "runtime.tick.failed",
                        Message           = exception.Message,
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                ExceptionType = exception.GetType().FullName,
                                exception.StackTrace,
                            }
                        ),
                    }, stoppingToken
                );
            }

            tickStopwatch.Stop();
            summarySampleCount++;
            summaryDurationTicks += tickStopwatch.Elapsed.Ticks;
            summaryMaximumTicks = Math.Max(summaryMaximumTicks, tickStopwatch.Elapsed.Ticks);
            await durationDiagnostics.RecordIfSlowAsync(
                "engine",
                "synchronization_tick",
                tickStopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.SynchronizationSlowThreshold,
                outcome
            );

            var now = DateTimeOffset.UtcNow;
            if (now - summaryStartedAt >= DurationSummaryInterval)
            {
                await durationDiagnostics.WriteSynchronizationSummaryAsync(
                    summarySampleCount,
                    TimeSpan.FromTicks(summaryDurationTicks / Math.Max(1, summarySampleCount)),
                    TimeSpan.FromTicks(summaryMaximumTicks),
                    now - summaryStartedAt
                );
                summaryStartedAt = now;
                summaryDurationTicks = 0;
                summaryMaximumTicks = 0;
                summarySampleCount = 0;
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
