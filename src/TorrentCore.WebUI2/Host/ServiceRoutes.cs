namespace TorrentCore.WebUI2.Host;

public static class ServiceRoutes
{
    public static bool IsAllowed(string method, string path)
    {
        var parts = path.Split('/');
        if (parts.Length < 2 || parts[0] != "api" || parts.Any(x => x.Length == 0 || x is "." or ".." || x.Contains('\\'))) return false;
        if (method == "GET")
        {
            if (path is "api/health" or "api/host/status" or "api/host/dashboard-lifecycle" or "api/host/runtime-settings"
                or "api/categories" or "api/torrents" or "api/history" or "api/history/filter-options" or "api/logs" or "api/logs/filter-options") return true;
            if (parts.Length == 4 && parts[1] == "history" && parts[2] == "by-torrent") return Guid.TryParse(parts[3], out _);
            if (parts.Length is 3 or 4 && parts[1] == "torrents" && Guid.TryParse(parts[2], out _))
                return parts.Length == 3 || parts[3] is "peers" or "trackers";
        }
        if (method == "PUT") return path == "api/host/runtime-settings" || parts.Length == 3 && parts[1] == "categories";
        if (method != "POST") return false;
        if (path is "api/torrents" or "api/host/restart-service" or "api/logs/delete-orphaned-torrent-logs"
            or "api/maintenance/logs/cleanup" or "api/maintenance/history/cleanup") return true;
        if (parts.Length is not (4 or 5) || parts[1] != "torrents" || !Guid.TryParse(parts[2], out _)) return false;
        return parts.Length == 4
            ? parts[3] is "pause" or "resume" or "make-next" or "hold" or "release-hold" or "resume-next" or "resume-on-hold" or "remove"
            : parts[3] == "metadata" && parts[4] is "refresh" or "reset" || parts[3] == "completion-callback" && parts[4] == "retry";
    }
}
