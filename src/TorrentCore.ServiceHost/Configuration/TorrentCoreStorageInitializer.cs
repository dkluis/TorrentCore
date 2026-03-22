namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreStorageInitializer(ResolvedTorrentCoreServicePaths servicePaths,
    ILogger<TorrentCoreStorageInitializer>                                        logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(servicePaths.DownloadRootPath);
            Directory.CreateDirectory(servicePaths.StorageRootPath);
            logger.LogInformation(
                "TorrentCore storage directories are ready. DownloadRootPath={DownloadRootPath}, StorageRootPath={StorageRootPath}",
                servicePaths.DownloadRootPath, servicePaths.StorageRootPath
            );
            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                NotSupportedException)
        {
            throw new InvalidOperationException(
                $"TorrentCore service could not initialize required storage directories. DownloadRootPath='{servicePaths.DownloadRootPath}', StorageRootPath='{servicePaths.StorageRootPath}'.",
                exception
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
}
