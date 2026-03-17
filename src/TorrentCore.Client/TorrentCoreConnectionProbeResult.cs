namespace TorrentCore.Client;

public sealed class TorrentCoreConnectionProbeResult
{
    public string? BaseUrl { get; init; }

    public bool IsConfigured { get; init; }

    public bool IsReachable { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }
}
