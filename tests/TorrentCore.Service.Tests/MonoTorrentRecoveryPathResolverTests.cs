using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentRecoveryPathResolverTests
{
    [Fact]
    public void ResolveDownloadRootPath_UsesPersistedDownloadRootPathWhenPresent()
    {
        var snapshot = new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "TorrentName",
            State = TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=TorrentName",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            DownloadRootPath = "/Volumes/Data/CustomDownloads",
            SavePath = "/Users/example/TorrentCore/downloads/TorrentName",
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
            TotalBytes = null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow,
        };

        var resolved = MonoTorrentRecoveryPathResolver.ResolveDownloadRootPath(snapshot, "/Users/example/TorrentCore/downloads");

        Assert.Equal("/Volumes/Data/CustomDownloads", resolved);
    }

    [Fact]
    public void ResolveDownloadRootPath_FallsBackToConfiguredDownloadRootForLegacySnapshot()
    {
        var snapshot = new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "TorrentName",
            State = TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB&dn=TorrentName",
            InfoHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            SavePath = "/Users/example/TorrentCore/downloads/TorrentName",
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
            TotalBytes = null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow,
        };

        var resolved = MonoTorrentRecoveryPathResolver.ResolveDownloadRootPath(snapshot, "/Users/example/TorrentCore/downloads");

        Assert.Equal("/Users/example/TorrentCore/downloads", resolved);
    }
}
