namespace TorrentCore.Contracts.History;

public sealed class TorrentPayloadRejectionDto
{
    public required string Reason { get; init; }
    public required string Phase { get; init; }
    public required IReadOnlyList<string> OffendingFiles { get; init; }
    public required IReadOnlyList<string> ObservedFiles { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
}
