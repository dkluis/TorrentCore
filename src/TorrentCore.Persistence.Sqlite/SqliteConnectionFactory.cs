using Microsoft.Data.Sqlite;

namespace TorrentCore.Persistence.Sqlite;

internal static class SqliteConnectionFactory
{
    internal static async Task<SqliteConnection> OpenAsync(
        string databaseFilePath,
        CancellationToken cancellationToken)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30,
            Pooling = true,
        };
        var connection = new SqliteConnection(connectionStringBuilder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
