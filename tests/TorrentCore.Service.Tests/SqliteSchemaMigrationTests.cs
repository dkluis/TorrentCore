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

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], versions);
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

    [Fact]
    public async Task Startup_UpgradesLegacyTorrentsSchema()
    {
        var rootPath = CreateTempRootPath("torrentcore-legacy-torrent-migrations");
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
                CREATE TABLE torrents (
                    torrent_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    state TEXT NOT NULL,
                    magnet_uri TEXT NOT NULL,
                    info_hash TEXT NULL,
                    save_path TEXT NOT NULL,
                    progress_percent REAL NOT NULL,
                    downloaded_bytes INTEGER NOT NULL,
                    total_bytes INTEGER NULL,
                    download_rate_bytes_per_second INTEGER NOT NULL,
                    upload_rate_bytes_per_second INTEGER NOT NULL,
                    tracker_count INTEGER NOT NULL,
                    connected_peer_count INTEGER NOT NULL,
                    added_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT NULL,
                    last_activity_at_utc TEXT NULL,
                    error_message TEXT NULL
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
        pragmaCommand.CommandText = "PRAGMA table_info(torrents);";

        var columns = new List<string>();
        await using var reader = await pragmaCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("download_root_path", columns);
        Assert.Contains("uploaded_bytes", columns);
        Assert.Contains("seeding_started_at_utc", columns);
        Assert.Contains("desired_state", columns);
        Assert.Contains("category_key", columns);
    }

    [Fact]
    public async Task Startup_CreatesRuntimeSettingsTable()
    {
        var rootPath = CreateTempRootPath("torrentcore-runtime-settings");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var command = verifyConnection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'runtime_settings';";
        var tableName = await command.ExecuteScalarAsync();

        Assert.Equal("runtime_settings", tableName);
    }

    [Fact]
    public async Task Startup_CreatesTorrentCategoriesTable()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-settings");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/categories");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var command = verifyConnection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'torrent_categories';";
        var tableName = await command.ExecuteScalarAsync();

        Assert.Equal("torrent_categories", tableName);
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
