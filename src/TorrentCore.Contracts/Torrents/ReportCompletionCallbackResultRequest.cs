namespace TorrentCore.Contracts.Torrents;

public sealed class ReportCompletionCallbackResultRequest
{
    public required Guid TorrentId { get; init; }
    public string TorrentHash { get; init; } = string.Empty;
    public DateTimeOffset? CompletionTimestamp { get; init; }
    public string CallbackSource { get; init; } = string.Empty;
    public string CallbackMachine { get; init; } = string.Empty;
    public string ContractVersion { get; init; } = string.Empty;
    public string FinalState { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string SourceState { get; init; } = string.Empty;
    public string ResubmitAdvice { get; init; } = string.Empty;
    public bool CallbackFinished { get; init; }
    public bool MediaConsideredDone { get; init; }
    public bool AllowResubmit { get; init; }
    public bool NeedsManualIntervention { get; init; }
    public string DisplayMessage { get; init; } = string.Empty;
    public string DetailMessage { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset? CallbackLocalTimestamp { get; init; }
    public int AttemptCount { get; init; }
    public string RawResponseJson { get; init; } = string.Empty;
}
