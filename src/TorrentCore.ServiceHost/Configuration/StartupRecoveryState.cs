namespace TorrentCore.Service.Configuration;

public sealed class StartupRecoveryState
{
    private readonly object _syncRoot = new();

    public bool Completed { get; private set; }
    public int RecoveredTorrentCount { get; private set; }
    public int NormalizedTorrentCount { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void MarkCompleted(int recoveredTorrentCount, int normalizedTorrentCount, DateTimeOffset completedAtUtc)
    {
        lock (_syncRoot)
        {
            Completed = true;
            RecoveredTorrentCount = recoveredTorrentCount;
            NormalizedTorrentCount = normalizedTorrentCount;
            CompletedAtUtc = completedAtUtc;
        }
    }
}
