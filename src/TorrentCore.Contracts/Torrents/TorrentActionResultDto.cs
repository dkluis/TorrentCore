namespace TorrentCore.Contracts.Torrents;

public sealed class TorrentActionResultDto
{
    public required Guid TorrentId { get; init; }
    public required string Action { get; init; }
    public required TorrentState State { get; init; }
    public required DateTimeOffset ProcessedAtUtc { get; init; }
    public bool DataDeleted { get; init; }
}
