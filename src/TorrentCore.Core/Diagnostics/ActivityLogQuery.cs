namespace TorrentCore.Core.Diagnostics;

public sealed class ActivityLogQuery
{
    public int               Take              { get; init; } = 100;
    public ActivityLogLevel? Level             { get; init; }
    public string?           Category          { get; init; }
    public string?           EventType         { get; init; }
    public Guid?             TorrentId         { get; init; }
    public Guid?             ServiceInstanceId { get; init; }
    public DateTimeOffset?   FromUtc           { get; init; }
    public DateTimeOffset?   ToUtc             { get; init; }
}
