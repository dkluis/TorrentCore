namespace TorrentCore.Persistence.Sqlite.Configuration;

public sealed class PersistedRuntimeSettingsRecord
{
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
