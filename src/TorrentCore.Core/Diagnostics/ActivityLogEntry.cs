namespace TorrentCore.Core.Diagnostics;

public sealed class ActivityLogEntry
{
    public required long             LogEntryId        { get; init; }
    public required DateTimeOffset   OccurredAtUtc     { get; init; }
    public required ActivityLogLevel Level             { get; init; }
    public required string           Category          { get; init; }
    public required string           EventType         { get; init; }
    public required string           Message           { get; init; }
    public          Guid?            TorrentId         { get; init; }
    public          Guid?            ServiceInstanceId { get; init; }
    public          string?          TraceId           { get; init; }
    public          string?          DetailsJson       { get; init; }
}
