namespace TorrentCore.Contracts.Diagnostics;

public sealed class DeleteOrphanedTorrentLogsResultDto
{
    public int DeletedLogEntryCount { get; init; }
}
