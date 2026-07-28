using Microsoft.Extensions.Logging.Abstractions;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentRemovalCleanupServiceTests
{
    [Fact]
    public async Task ScheduleDeleteDataCleanup_RemovesPayloadArtifactsAndEmptyDirectories()
    {
        var rootPath = CreateTempRootPath("torrentcore-cleanup-service");
        var downloadRootPath = Path.Combine(rootPath, "downloads");
        var payloadDirectory = Path.Combine(downloadRootPath, "Show", "Season 01");
        Directory.CreateDirectory(payloadDirectory);
        var payloadFile = Path.Combine(payloadDirectory, "Episode 01.mkv");
        File.WriteAllText(payloadFile, "payload");

        var service = new TorrentRemovalCleanupService(
            NullLogger<TorrentRemovalCleanupService>.Instance,
            new RecordingActivityLogService(),
            new ServiceInstanceContext()
        );

        service.ScheduleDeleteDataCleanup(Guid.NewGuid(), downloadRootPath, [payloadFile, payloadDirectory]);

        var deleted = await WaitForAsync(
            () => Task.FromResult(!File.Exists(payloadFile) && !Directory.Exists(payloadDirectory)),
            value => value,
            timeout: TimeSpan.FromSeconds(5)
        );

        Assert.True(deleted);
        Assert.False(Directory.Exists(Path.Combine(downloadRootPath, "Show")));
        Assert.True(Directory.Exists(downloadRootPath));
    }

    [Fact]
    public async Task TryDeleteDataWithRetryAsync_RetriesTransientIoFailures()
    {
        var attempts = 0;
        var observedDelays = new List<TimeSpan>();

        var exception = await TorrentRemovalCleanupService.TryDeleteDataWithRetryAsync(
            "/downloads",
            ["/downloads/payload.bin"],
            (_, _) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException("Resource busy.");
                }
            },
            delay =>
            {
                observedDelays.Add(delay);
                return Task.CompletedTask;
            }
        );

        Assert.Null(exception);
        Assert.Equal(3, attempts);
        Assert.Equal(TorrentRemovalCleanupService.RetryDelays.Take(2), observedDelays);
    }

    [Fact]
    public async Task TryDeleteDataWithRetryAsync_DoesNotRetryNonIoFailure()
    {
        var attempts = 0;

        var exception = await TorrentRemovalCleanupService.TryDeleteDataWithRetryAsync(
            "/downloads",
            ["/downloads/payload.bin"],
            (_, _) =>
            {
                attempts++;
                throw new InvalidOperationException("Invalid cleanup request.");
            },
            _ => Task.CompletedTask
        );

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(1, attempts);
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> predicate,
        TimeSpan timeout,
        int pollIntervalMilliseconds = 50
    )
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            var result = await action();
            if (predicate(result))
            {
                return result;
            }

            await Task.Delay(pollIntervalMilliseconds);
        }

        throw new TimeoutException("Timed out waiting for the expected condition.");
    }

    private static string CreateTempRootPath(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingActivityLogService : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(
            ActivityLogQuery query,
            CancellationToken cancellationToken
        )
            => Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
