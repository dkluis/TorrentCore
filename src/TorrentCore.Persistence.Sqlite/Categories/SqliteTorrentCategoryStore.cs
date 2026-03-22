#region

using System.Globalization;
using Microsoft.Data.Sqlite;
using TorrentCore.Core.Categories;

#endregion

namespace TorrentCore.Persistence.Sqlite.Categories;

public sealed class SqliteTorrentCategoryStore(string databaseFilePath) : ITorrentCategoryStore
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool          _isInitialized;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_isInitialized)
            {
                return;
            }

            var directoryPath = Path.GetDirectoryName(databaseFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<IReadOnlyList<TorrentCategoryDefinition>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  category_key,
                                  display_name,
                                  callback_label,
                                  download_root_path,
                                  enabled,
                                  invoke_completion_callback,
                                  sort_order,
                                  updated_at_utc
                              FROM torrent_categories
                              ORDER BY sort_order ASC, category_key ASC;
                              """;

        return await ReadCategoriesAsync(command, cancellationToken);
    }

    public async Task<TorrentCategoryDefinition?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT
                                  category_key,
                                  display_name,
                                  callback_label,
                                  download_root_path,
                                  enabled,
                                  invoke_completion_callback,
                                  sort_order,
                                  updated_at_utc
                              FROM torrent_categories
                              WHERE category_key = $category_key
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$category_key", key);

        var results = await ReadCategoriesAsync(command, cancellationToken);
        return results.SingleOrDefault();
    }

    public async Task UpsertAsync(TorrentCategoryDefinition category, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO torrent_categories (
                                  category_key,
                                  display_name,
                                  callback_label,
                                  download_root_path,
                                  enabled,
                                  invoke_completion_callback,
                                  sort_order,
                                  updated_at_utc
                              )
                              VALUES (
                                  $category_key,
                                  $display_name,
                                  $callback_label,
                                  $download_root_path,
                                  $enabled,
                                  $invoke_completion_callback,
                                  $sort_order,
                                  $updated_at_utc
                              )
                              ON CONFLICT(category_key) DO UPDATE SET
                                  display_name = excluded.display_name,
                                  callback_label = excluded.callback_label,
                                  download_root_path = excluded.download_root_path,
                                  enabled = excluded.enabled,
                                  invoke_completion_callback = excluded.invoke_completion_callback,
                                  sort_order = excluded.sort_order,
                                  updated_at_utc = excluded.updated_at_utc;
                              """;

        command.Parameters.AddWithValue("$category_key",               category.Key);
        command.Parameters.AddWithValue("$display_name",               category.DisplayName);
        command.Parameters.AddWithValue("$callback_label",             category.CallbackLabel);
        command.Parameters.AddWithValue("$download_root_path",         category.DownloadRootPath);
        command.Parameters.AddWithValue("$enabled",                    category.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$invoke_completion_callback", category.InvokeCompletionCallback ? 1 : 0);
        command.Parameters.AddWithValue("$sort_order",                 category.SortOrder);
        command.Parameters.AddWithValue(
            "$updated_at_utc", category.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        );
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TorrentCategoryDefinition>> ReadCategoriesAsync(SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var             results = new List<TorrentCategoryDefinition>();
        await using var reader  = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new TorrentCategoryDefinition
                {
                    Key                      = reader.GetString(0),
                    DisplayName              = reader.GetString(1),
                    CallbackLabel            = reader.GetString(2),
                    DownloadRootPath         = reader.GetString(3),
                    Enabled                  = reader.GetInt64(4) != 0,
                    InvokeCompletionCallback = reader.GetInt64(5) != 0,
                    SortOrder                = reader.GetInt32(6),
                    UpdatedAtUtc = DateTimeOffset.Parse(
                        reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                }
            );
        }

        return results;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode       = SqliteOpenMode.ReadWriteCreate,
        };

        return new SqliteConnection(connectionStringBuilder.ToString());
    }
}
