namespace TorrentCore.Service.Infrastructure;

public sealed class ServiceRestartScheduleResult
{
    public required string ServiceLabel { get; init; }
    public required string Message { get; init; }
}
