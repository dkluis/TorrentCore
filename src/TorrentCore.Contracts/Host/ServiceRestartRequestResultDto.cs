namespace TorrentCore.Contracts.Host;

public sealed class ServiceRestartRequestResultDto
{
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string ServiceLabel { get; init; }
    public required string Message { get; init; }
}
