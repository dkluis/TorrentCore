namespace TorrentCore.Service.Configuration;

public static class TorrentCoreServicePathResolver
{
    public static ResolvedTorrentCoreServicePaths Resolve(string contentRootPath, TorrentCoreServiceOptions options)
    {
        return new ResolvedTorrentCoreServicePaths
        {
            DownloadRootPath = ResolveAbsolutePath(contentRootPath, options.DownloadRootPath),
            StorageRootPath  = ResolveAbsolutePath(contentRootPath, options.StorageRootPath),
            DatabaseFilePath = ResolveAbsolutePath(contentRootPath, Path.Combine(options.StorageRootPath, "torrentcore.db")),
        };
    }

    private static string ResolveAbsolutePath(string contentRootPath, string configuredPath)
    {
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath);

        return Path.GetFullPath(path);
    }
}
