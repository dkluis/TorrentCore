using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentEngineAdapterStateNormalizationTests
{
    [Fact]
    public void ResolveCompletedAtUtc_IgnoresTransientPreHashCompletionUntilSeeding()
    {
        var submittedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        var completedAtUtc = DateTimeOffset.UtcNow;

        var queuedCompletion = MonoTorrentEngineAdapter.ResolveCompletedAtUtc(
            submittedAtUtc,
            TorrentState.Queued,
            completedAtUtc
        );
        var downloadingCompletion = MonoTorrentEngineAdapter.ResolveCompletedAtUtc(
            submittedAtUtc,
            TorrentState.Downloading,
            completedAtUtc
        );
        var seedingCompletion = MonoTorrentEngineAdapter.ResolveCompletedAtUtc(
            null,
            TorrentState.Seeding,
            completedAtUtc
        );

        Assert.Null(queuedCompletion);
        Assert.Null(downloadingCompletion);
        Assert.Equal(completedAtUtc, seedingCompletion);
    }

    [Fact]
    public void NormalizeCompletedErrorSnapshot_ConvertsVisibleCompletedErrorToCompleted()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(
            state: TorrentState.Error,
            progressPercent: 100,
            downloadedBytes: 100,
            totalBytes: 100,
            completedAtUtc: now.AddMinutes(-2),
            seedingStartedAtUtc: now.AddMinutes(-1),
            errorMessage: "ReadFailure"
        );

        var normalized = MonoTorrentEngineAdapter.NormalizeCompletedErrorSnapshot(
            snapshot, finalPayloadVisible: true, now
        );

        Assert.Equal(TorrentState.Completed, normalized.State);
        Assert.Equal(100d, normalized.ProgressPercent);
        Assert.Equal(0, normalized.ConnectedPeerCount);
        Assert.Equal(0, normalized.DownloadRateBytesPerSecond);
        Assert.Equal(0, normalized.UploadRateBytesPerSecond);
        Assert.Null(normalized.ErrorMessage);
        Assert.Equal(now.AddMinutes(-2), normalized.CompletedAtUtc);
        Assert.Equal(now.AddMinutes(-1), normalized.SeedingStartedAtUtc);
    }

    [Fact]
    public void NormalizeCompletedErrorSnapshot_KeepsErrorWhenFinalPayloadIsNotVisible()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(
            state: TorrentState.Error,
            progressPercent: 100,
            downloadedBytes: 100,
            totalBytes: 100,
            completedAtUtc: now.AddMinutes(-1),
            seedingStartedAtUtc: null,
            errorMessage: "ReadFailure"
        );

        var normalized = MonoTorrentEngineAdapter.NormalizeCompletedErrorSnapshot(
            snapshot, finalPayloadVisible: false, now
        );

        Assert.Equal(TorrentState.Error, normalized.State);
        Assert.Equal("ReadFailure", normalized.ErrorMessage);
    }

    [Fact]
    public void NormalizeCompletedErrorSnapshot_KeepsErrorWhenTransferIsIncomplete()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateSnapshot(
            state: TorrentState.Error,
            progressPercent: 72,
            downloadedBytes: 72,
            totalBytes: 100,
            completedAtUtc: null,
            seedingStartedAtUtc: null,
            errorMessage: "ReadFailure"
        );

        var normalized = MonoTorrentEngineAdapter.NormalizeCompletedErrorSnapshot(
            snapshot, finalPayloadVisible: true, now
        );

        Assert.Equal(TorrentState.Error, normalized.State);
        Assert.Equal("ReadFailure", normalized.ErrorMessage);
        Assert.Null(normalized.CompletedAtUtc);
    }

    private static TorrentSnapshot CreateSnapshot(TorrentState state, double progressPercent, long downloadedBytes,
        long? totalBytes, DateTimeOffset? completedAtUtc, DateTimeOffset? seedingStartedAtUtc, string? errorMessage)
    {
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Example",
            State = state,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Example",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            DownloadRootPath = "/tmp",
            SavePath = "/tmp/Example",
            ProgressPercent = progressPercent,
            DownloadedBytes = downloadedBytes,
            UploadedBytes = 50,
            TotalBytes = totalBytes,
            DownloadRateBytesPerSecond = 123,
            UploadRateBytesPerSecond = 456,
            TrackerCount = 2,
            ConnectedPeerCount = 7,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = completedAtUtc,
            SeedingStartedAtUtc = seedingStartedAtUtc,
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5),
            ErrorMessage = errorMessage,
        };
    }
}
