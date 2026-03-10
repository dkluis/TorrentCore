using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class SqliteSchemaMigrationTests
{
    [Fact]
    public async Task Startup_CreatesSchemaMigrationsTable_AndRecordsAppliedVersions()
    {
        var rootPath = CreateTempRootPath("torrentcore-migrations");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";

        var versions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }

        Assert.Equal([1, 2, 3], versions);
    }

    [Fact]
    public async Task Startup_UpgradesLegacyActivityLogsSchema()
    {
        var rootPath = CreateTempRootPath("torrentcore-legacy-migrations");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        Directory.CreateDirectory(storagePath);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE activity_logs (
                    log_entry_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_at_utc TEXT NOT NULL,
                    level TEXT NOT NULL,
                    category TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    torrent_id TEXT NULL,
                    trace_id TEXT NULL,
                    details_json TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var pragmaCommand = verifyConnection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(activity_logs);";

        var columns = new List<string>();
        await using var reader = await pragmaCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("service_instance_id", columns);
    }

    private static WebApplicationFactory<Program> CreateFactory(string downloadPath, string storagePath)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = downloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = storagePath,
                    });
                });
            });
    }

    private static string CreateTempRootPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }
}
