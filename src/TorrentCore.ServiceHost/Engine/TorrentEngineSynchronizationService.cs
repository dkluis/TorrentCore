#region

using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class TorrentEngineSynchronizationService(ITorrentEngineAdapter torrentEngineAdapter,
    IOptions<TorrentCoreServiceOptions> serviceOptions, IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext) : BackgroundService
{
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_serviceOptions.RuntimeTickIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
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

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
