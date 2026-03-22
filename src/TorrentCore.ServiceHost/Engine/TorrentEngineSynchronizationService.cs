#region

using Microsoft.Extensions.Options;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class TorrentEngineSynchronizationService(ITorrentEngineAdapter torrentEngineAdapter,
    IOptions<TorrentCoreServiceOptions>                                       serviceOptions) : BackgroundService
{
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_serviceOptions.RuntimeTickIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            await torrentEngineAdapter.SynchronizeAsync(stoppingToken);

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
