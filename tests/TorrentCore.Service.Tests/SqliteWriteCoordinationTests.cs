using Microsoft.Data.Sqlite;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Persistence.Sqlite.Logging;
using TorrentCore.Persistence.Sqlite.Schema;

namespace TorrentCore.Service.Tests;

public sealed class SqliteWriteCoordinationTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        $"torrentcore-write-coordination-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SeparateLogServices_CoordinateConcurrentWrites_InWalMode()
    {
        Directory.CreateDirectory(_rootPath);
        var databaseFilePath = Path.Combine(_rootPath, "torrentcore.db");
        await new SqliteSchemaMigrator(databaseFilePath).ApplyMigrationsAsync(CancellationToken.None);
        var firstService = new SqliteActivityLogService(databaseFilePath, 1000);
        var secondService = new SqliteActivityLogService(databaseFilePath, 1000);

        var writes = Enumerable.Range(0, 100).Select(
            index => (index % 2 == 0 ? firstService : secondService).WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "test",
                    EventType = "test.concurrent_write",
                    Message = $"Concurrent write {index}.",
                },
                CancellationToken.None
            )
        );
        await Task.WhenAll(writes);

        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM activity_logs WHERE event_type = 'test.concurrent_write';";
        Assert.Equal(100L, Assert.IsType<long>(await countCommand.ExecuteScalarAsync()));
        var journalModeCommand = connection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", Assert.IsType<string>(await journalModeCommand.ExecuteScalarAsync()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
