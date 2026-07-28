using System.Globalization;
using Microsoft.Data.Sqlite;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Logging;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class SqliteActivityLogServiceTests
{
    [Fact]
    public async Task DeleteInactiveBefore_UsesExclusiveCutoff_AndProtectsLiveTorrents()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-log-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var activeTorrentId = Guid.NewGuid();
            var orphanedTorrentId = Guid.NewGuid();
            var cutoffUtc = new DateTimeOffset(2026, 7, 20, 4, 0, 0, TimeSpan.Zero);

            var torrentStore = new SqliteTorrentStateStore(databaseFilePath);
            await torrentStore.InsertAsync(CreateSnapshot(activeTorrentId), CancellationToken.None);

            await InsertLogAsync(databaseFilePath, "old-service", cutoffUtc.AddMinutes(-1), null);
            await InsertLogAsync(databaseFilePath, "old-orphan", cutoffUtc.AddMinutes(-1), orphanedTorrentId);
            await InsertLogAsync(databaseFilePath, "old-live", cutoffUtc.AddMinutes(-1), activeTorrentId);
            await InsertLogAsync(databaseFilePath, "at-cutoff", cutoffUtc, null);
            await InsertLogAsync(databaseFilePath, "new-service", cutoffUtc.AddMinutes(1), null);

            var service = new SqliteActivityLogService(databaseFilePath, 1_000);
            var deletedCount = await service.DeleteInactiveBeforeAsync(cutoffUtc, CancellationToken.None);

            Assert.Equal(2, deletedCount);
            Assert.Equal(
                ["at-cutoff", "new-service", "old-live"],
                await ReadEventTypesAsync(databaseFilePath)
            );
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static async Task InsertLogAsync(string databaseFilePath, string eventType,
        DateTimeOffset occurredAtUtc, Guid? torrentId)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO activity_logs (
                                  occurred_at_utc,
                                  level,
                                  category,
                                  event_type,
                                  message,
                                  torrent_id
                              )
                              VALUES (
                                  $occurred_at_utc,
                                  'Information',
                                  'test',
                                  $event_type,
                                  $event_type,
                                  $torrent_id
                              );
                              """;
        command.Parameters.AddWithValue(
            "$occurred_at_utc", occurredAtUtc.ToString("O", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue("$event_type", eventType);
        command.Parameters.AddWithValue("$torrent_id", torrentId?.ToString() ?? (object) DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadEventTypesAsync(string databaseFilePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT event_type FROM activity_logs ORDER BY event_type;";

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static TorrentSnapshot CreateSnapshot(Guid torrentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = "Protected Live Torrent",
            State = TorrentState.Downloading,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:3333333333333333333333333333333333333333",
            InfoHash = "3333333333333333333333333333333333333333",
            SavePath = "/tmp/torrentcore-tests/protected-live-torrent",
            ProgressPercent = 50,
            DownloadedBytes = 512,
            UploadedBytes = 0,
            TotalBytes = 1_024,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = now,
            LastActivityAtUtc = now,
        };
    }
}
