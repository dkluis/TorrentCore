namespace TorrentCore.Service.Engine;

public static class MonoTorrentSavePathNormalizer
{
    public static string Normalize(string savePath, string? torrentName)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return savePath;
        }

        var normalizedPath = Path.GetFullPath(savePath);
        if (string.IsNullOrWhiteSpace(torrentName))
        {
            return normalizedPath;
        }

        var trailingSegment = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(trailingSegment, torrentName, StringComparison.Ordinal))
        {
            return normalizedPath;
        }

        var parentPath = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return normalizedPath;
        }

        var parentSegment = Path.GetFileName(parentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(parentSegment, torrentName, StringComparison.Ordinal)
            ? parentPath
            : normalizedPath;
    }
}
