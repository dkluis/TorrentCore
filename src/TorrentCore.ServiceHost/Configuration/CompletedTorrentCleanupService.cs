#region

using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

#endregion

namespace TorrentCore.Service.Configuration;

public sealed class CompletedTorrentCleanupService(ITorrentStateStore torrentStateStore,
    ITorrentEngineAdapter torrentEngineAdapter, IRuntimeSettingsService runtimeSettingsService,
    IActivityLogService activityLogService, ServiceInstanceContext serviceInstanceContext,
    IOptions<TorrentCoreServiceOptions> serviceOptions, ILogger<CompletedTorrentCleanupService> logger)
        : BackgroundService
{
    private readonly SemaphoreSlim             _gate           = new(1, 1);
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_serviceOptions.RuntimeTickIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Completed torrent cleanup tick failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (runtimeSettings.CompletedTorrentCleanupMode != CompletedTorrentCleanupMode.AfterCompletedMinutes)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now      = DateTimeOffset.UtcNow;
            var torrents = await torrentStateStore.ListAsync(cancellationToken);
            var candidates = torrents
                            .Where(torrent
                                     => torrent.State == TorrentState.Completed && torrent.CompletedAtUtc is not null
                             )
                            .Where(torrent
                                     => (now - torrent.CompletedAtUtc!.Value).TotalMinutes >=
                                     runtimeSettings.CompletedTorrentCleanupMinutes
                             )
                            .OrderBy(torrent => torrent.CompletedAtUtc)
                            .ThenBy(torrent => torrent.TorrentId)
                            .ToList();

            foreach (var torrent in candidates)
            {
                try
                {
                    await torrentEngineAdapter.RemoveAsync(
                        torrent.TorrentId, new RemoveTorrentRequest
                        {
                            DeleteData = false,
                        }, cancellationToken
                    );

                    await activityLogService.WriteAsync(
                        new ActivityLogWriteRequest
                        {
                            Level     = ActivityLogLevel.Information,
                            Category  = "torrent",
                            EventType = "torrent.cleanup.auto_removed",
                            Message =
                                    $"Automatically removed completed torrent '{torrent.Name}' from TorrentCore tracking.",
                            TorrentId         = torrent.TorrentId,
                            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                            DetailsJson = JsonSerializer.Serialize(
                                new
                                {
                                    Mode = runtimeSettings.CompletedTorrentCleanupMode,
                                    runtimeSettings.CompletedTorrentCleanupMinutes,
                                    DeleteData = false,
                                    torrent.CompletedAtUtc,
                                }
                            ),
                        }, cancellationToken
                    );
                }
                catch (Application.ServiceOperationException exception) when (exception.Code == "torrent_not_found")
                {
                    logger.LogDebug(
                        "Completed torrent {TorrentId} was already removed before cleanup executed.", torrent.TorrentId
                    );
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception, "Automatic cleanup failed for completed torrent {TorrentId}", torrent.TorrentId
                    );

                    await activityLogService.WriteAsync(
                        new ActivityLogWriteRequest
                        {
                            Level             = ActivityLogLevel.Warning,
                            Category          = "torrent",
                            EventType         = "torrent.cleanup.auto_remove.failed",
                            Message           = exception.Message,
                            TorrentId         = torrent.TorrentId,
                            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                            DetailsJson = JsonSerializer.Serialize(
                                new
                                {
                                    Mode = runtimeSettings.CompletedTorrentCleanupMode,
                                    runtimeSettings.CompletedTorrentCleanupMinutes,
                                    DeleteData    = false,
                                    ExceptionType = exception.GetType().FullName,
                                }
                            ),
                        }, cancellationToken
                    );
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}
