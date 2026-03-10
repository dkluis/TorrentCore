using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Engine;

public static class MonoTorrentRecoveryPathResolver
{
    public static string ResolveDownloadRootPath(TorrentSnapshot snapshot, string configuredDownloadRootPath)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DownloadRootPath))
        {
            return Path.GetFullPath(snapshot.DownloadRootPath);
        }

        return Path.GetFullPath(configuredDownloadRootPath);
    }
}
