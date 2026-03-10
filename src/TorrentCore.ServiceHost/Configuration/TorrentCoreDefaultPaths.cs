namespace TorrentCore.Service.Configuration;

public static class TorrentCoreDefaultPaths
{
    public static string GetDefaultDownloadRootPath()
    {
        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            return Path.Combine(AppContext.BaseDirectory, "downloads");
        }

        return Path.Combine(userProfilePath, "TorrentCore", "downloads");
    }

    public static string GetDefaultStorageRootPath()
    {
        var localApplicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationDataPath))
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfilePath))
            {
                return Path.Combine(AppContext.BaseDirectory, "storage");
            }

            return Path.Combine(userProfilePath, ".torrentcore", "storage");
        }

        return Path.Combine(localApplicationDataPath, "TorrentCore", "storage");
    }
}
