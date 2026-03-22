namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionFinalizationCheckResult
{
    public required bool    IsReady          { get; init; }
    public required string  FinalPayloadPath { get; init; }
    public          string? PendingReason    { get; init; }
}
