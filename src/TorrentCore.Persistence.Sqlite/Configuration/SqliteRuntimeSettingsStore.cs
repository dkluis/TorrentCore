#region

using System.Globalization;
using Microsoft.Data.Sqlite;

#endregion

namespace TorrentCore.Persistence.Sqlite.Configuration;

public sealed class SqliteRuntimeSettingsStore(string databaseFilePath)
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

    public async Task<PersistedRuntimeSettingsRecord> GetAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT setting_key, setting_value, updated_at_utc
                              FROM runtime_settings
                              ORDER BY setting_key;
                              """;

        var             values       = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateTimeOffset? updatedAtUtc = null;
        await using var reader       = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);

            var rowUpdatedAtUtc = DateTimeOffset.Parse(
                reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
            );
            if (updatedAtUtc is null || rowUpdatedAtUtc > updatedAtUtc)
            {
                updatedAtUtc = rowUpdatedAtUtc;
            }
        }

        return new PersistedRuntimeSettingsRecord
        {
            Values       = values,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    public async Task UpsertAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        foreach (var pair in values)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction) transaction;
            command.CommandText = """
                                  INSERT INTO runtime_settings (setting_key, setting_value, updated_at_utc)
                                  VALUES ($setting_key, $setting_value, $updated_at_utc)
                                  ON CONFLICT(setting_key) DO UPDATE SET
                                      setting_value = excluded.setting_value,
                                      updated_at_utc = excluded.updated_at_utc;
                                  """;
            command.Parameters.AddWithValue("$setting_key",    pair.Key);
            command.Parameters.AddWithValue("$setting_value",  pair.Value);
            command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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
