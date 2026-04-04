#region

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using TorrentCore.Core.Diagnostics;

#endregion

namespace TorrentCore.Persistence.Sqlite.Logging;

public sealed class SqliteActivityLogService(string databaseFilePath, int maxEntryCount) : IActivityLogService
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
            await EnforceRetentionAsync(connection, cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO activity_logs (
                                  occurred_at_utc,
                                  level,
                                  category,
                                  event_type,
                                  message,
                                  torrent_id,
                                  service_instance_id,
                                  trace_id,
                                  details_json
                              )
                              VALUES (
                                  $occurred_at_utc,
                                  $level,
                                  $category,
                                  $event_type,
                                  $message,
                                  $torrent_id,
                                  $service_instance_id,
                                  $trace_id,
                                  $details_json
                              );
                              """;

        command.Parameters.AddWithValue(
            "$occurred_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue("$level",      request.Level.ToString());
        command.Parameters.AddWithValue("$category",   request.Category);
        command.Parameters.AddWithValue("$event_type", request.EventType);
        command.Parameters.AddWithValue("$message",    request.Message);
        command.Parameters.AddWithValue("$torrent_id", request.TorrentId?.ToString() ?? (object) DBNull.Value);
        command.Parameters.AddWithValue(
            "$service_instance_id", request.ServiceInstanceId?.ToString() ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue("$trace_id",     request.TraceId     ?? (object) DBNull.Value);
        command.Parameters.AddWithValue("$details_json", request.DetailsJson ?? (object) DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnforceRetentionAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query,
        CancellationToken                                                              cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var boundedTake = Math.Max(1, query.Take);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT
                log_entry_id,
                occurred_at_utc,
                level,
                category,
                event_type,
                message,
                torrent_id,
                service_instance_id,
                trace_id,
                details_json
            FROM activity_logs
            """
        );
        var filters = new List<string>();

        if (query.Level is not null)
        {
            filters.Add("level = $level");
            command.Parameters.AddWithValue("$level", query.Level.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            filters.Add("category = $category");
            command.Parameters.AddWithValue("$category", query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            filters.Add("event_type = $event_type");
            command.Parameters.AddWithValue("$event_type", query.EventType);
        }

        if (query.TorrentId is not null)
        {
            filters.Add("torrent_id = $torrent_id");
            command.Parameters.AddWithValue("$torrent_id", query.TorrentId.Value.ToString());
        }

        if (query.ServiceInstanceId is not null)
        {
            filters.Add("service_instance_id = $service_instance_id");
            command.Parameters.AddWithValue("$service_instance_id", query.ServiceInstanceId.Value.ToString());
        }

        if (query.FromUtc is not null)
        {
            filters.Add("occurred_at_utc >= $from_utc");
            command.Parameters.AddWithValue(
                "$from_utc", query.FromUtc.Value.ToString("O", CultureInfo.InvariantCulture)
            );
        }

        if (query.ToUtc is not null)
        {
            filters.Add("occurred_at_utc <= $to_utc");
            command.Parameters.AddWithValue("$to_utc", query.ToUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        if (filters.Count > 0)
        {
            sql.AppendLine();
            sql.Append("WHERE ");
            sql.Append(string.Join(" AND ", filters));
        }

        sql.AppendLine();
        sql.AppendLine("ORDER BY occurred_at_utc DESC, log_entry_id DESC");
        sql.AppendLine("LIMIT $take;");

        command.CommandText = sql.ToString();
        command.Parameters.AddWithValue("$take", boundedTake);

        var             results = new List<ActivityLogEntry>();
        await using var reader  = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new ActivityLogEntry
                {
                    LogEntryId = reader.GetInt64(0),
                    OccurredAtUtc = DateTimeOffset.Parse(
                        reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    Level             = Enum.Parse<ActivityLogLevel>(reader.GetString(2), true),
                    Category          = reader.GetString(3),
                    EventType         = reader.GetString(4),
                    Message           = reader.GetString(5),
                    TorrentId         = reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                    ServiceInstanceId = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                    TraceId           = reader.IsDBNull(8) ? null : reader.GetString(8),
                    DetailsJson       = reader.IsDBNull(9) ? null : reader.GetString(9),
                }
            );
        }

        return results;
    }

    private async Task EnforceRetentionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var boundedMaxEntryCount = Math.Max(100, maxEntryCount);

        var command = connection.CreateCommand();
        command.CommandText = """
                              DELETE FROM activity_logs
                              WHERE log_entry_id NOT IN (
                                  SELECT log_entry_id
                                  FROM activity_logs
                                  ORDER BY occurred_at_utc DESC, log_entry_id DESC
                                  LIMIT $retain_count
                              );
                              """;
        command.Parameters.AddWithValue("$retain_count", boundedMaxEntryCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
