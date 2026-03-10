using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Engine;

public sealed class TorrentEngineRecoveryResult
{
    public required int RecoveredTorrentCount { get; init; }
    public required int NormalizedTorrentCount { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public IReadOnlyList<TorrentRecoveryChange> Changes { get; init; } = Array.Empty<TorrentRecoveryChange>();
}

public sealed class TorrentRecoveryChange
{
    public required Guid TorrentId { get; init; }
    public required string Name { get; init; }
    public required TorrentState PreviousState { get; init; }
    public required TorrentState CurrentState { get; init; }
}
