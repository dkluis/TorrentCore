using System.Collections.Concurrent;

namespace TorrentCore.Persistence.Sqlite;

internal static class SqliteWriteCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
            new(StringComparer.OrdinalIgnoreCase);

    internal static async ValueTask<IAsyncDisposable> AcquireAsync(
        string databaseFilePath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(databaseFilePath);
        var gate = Gates.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
