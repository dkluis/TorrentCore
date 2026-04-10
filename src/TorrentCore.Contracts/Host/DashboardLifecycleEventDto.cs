namespace TorrentCore.Contracts.Host;

public sealed class DashboardLifecycleEventDto
{
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string         Level         { get; init; }
    public required string         Category      { get; init; }
    public required string         EventType     { get; init; }
    public required string         Message       { get; init; }
    public          Guid?          TorrentId     { get; init; }
}
