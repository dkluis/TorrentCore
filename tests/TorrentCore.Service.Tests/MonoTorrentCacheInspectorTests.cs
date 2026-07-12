using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentCacheInspectorTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-cache-audit-{Guid.NewGuid():N}");

    [Fact]
    public void Inspect_ReportsInventoryPairsAndAgedCandidates()
    {
        var metadataPath = Path.Combine(_rootPath, "metadata");
        var fastResumePath = Path.Combine(_rootPath, "fastresume");
        Directory.CreateDirectory(metadataPath);
        Directory.CreateDirectory(fastResumePath);
        File.WriteAllText(Path.Combine(metadataPath, "AAA.torrent"), "metadata");
        File.WriteAllText(Path.Combine(metadataPath, "BBB.torrent"), "old metadata");
        File.WriteAllText(Path.Combine(fastResumePath, "AAA.fresume"), "resume");
        File.WriteAllText(Path.Combine(fastResumePath, "CCC.fresume"), "orphan resume");
        var inspectedAtUtc = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(
            Path.Combine(metadataPath, "BBB.torrent"), inspectedAtUtc.AddDays(-91).UtcDateTime
        );

        var audit = MonoTorrentCacheInspector.Inspect(_rootPath, inspectedAtUtc);

        Assert.Equal(4, audit.FileCount);
        Assert.Equal(2, audit.MetadataFileCount);
        Assert.Equal(2, audit.FastResumeFileCount);
        Assert.Equal(1, audit.MetadataWithoutFastResumeCount);
        Assert.Equal(1, audit.FastResumeWithoutMetadataCount);
        Assert.Equal(1, audit.AgedCandidateFileCount);
        Assert.Equal(90, audit.AgedCandidateDays);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
