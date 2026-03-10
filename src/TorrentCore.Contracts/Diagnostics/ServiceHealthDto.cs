namespace TorrentCore.Contracts.Diagnostics;

public sealed class ServiceHealthDto
{
    public required string ServiceName { get; init; }
    public required string Status { get; init; }
    public required string EnvironmentName { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }
}
