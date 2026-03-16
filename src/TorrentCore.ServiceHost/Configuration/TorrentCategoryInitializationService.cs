using Microsoft.Extensions.Logging;

namespace TorrentCore.Service.Configuration;

public sealed class TorrentCategoryInitializationService(
    ITorrentCategoryService torrentCategoryService,
    ILogger<TorrentCategoryInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await torrentCategoryService.EnsureDefaultCategoriesAsync(cancellationToken);
        logger.LogInformation("TorrentCore default categories are initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
