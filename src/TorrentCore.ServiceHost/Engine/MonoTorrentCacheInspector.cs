namespace TorrentCore.Service.Engine;

internal static class MonoTorrentCacheInspector
{
    internal static MonoTorrentCacheAudit Inspect(string cacheDirectory, DateTimeOffset inspectedAtUtc)
    {
        const int agedCandidateDays = 90;
        var agedBeforeUtc = inspectedAtUtc.AddDays(-agedCandidateDays);
        var files = Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories)
                             .Select(path => new FileInfo(path))
                             .ToArray();
        var metadataHashes = GetHashes(files, ".torrent");
        var fastResumeHashes = GetHashes(files, ".fresume");

        return new MonoTorrentCacheAudit(
            cacheDirectory,
            files.Length,
            files.Sum(file => file.Length),
            metadataHashes.Count,
            fastResumeHashes.Count,
            metadataHashes.Except(fastResumeHashes, StringComparer.OrdinalIgnoreCase).Count(),
            fastResumeHashes.Except(metadataHashes, StringComparer.OrdinalIgnoreCase).Count(),
            files.Count(file => file.LastWriteTimeUtc < agedBeforeUtc.UtcDateTime),
            agedCandidateDays,
            files.Length == 0 ? null : files.Min(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)),
            files.Length == 0 ? null : files.Max(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
        );
    }

    private static HashSet<string> GetHashes(IEnumerable<FileInfo> files, string extension)
    {
        return files.Where(file => string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase))
                    .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record MonoTorrentCacheAudit(
    string CacheDirectory,
    int FileCount,
    long TotalBytes,
    int MetadataFileCount,
    int FastResumeFileCount,
    int MetadataWithoutFastResumeCount,
    int FastResumeWithoutMetadataCount,
    int AgedCandidateFileCount,
    int AgedCandidateDays,
    DateTimeOffset? OldestLastWriteAtUtc,
    DateTimeOffset? NewestLastWriteAtUtc);
