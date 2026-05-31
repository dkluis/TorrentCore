namespace TorrentCore.Contracts.Torrents;

public sealed class CompletionCallbackFeedbackDto
{
    public required Guid            TorrentId                 { get; init; }
    public          string?         TorrentHash               { get; init; }
    public          DateTimeOffset? CompletionTimestamp       { get; init; }
    public          string?         CallbackSource            { get; init; }
    public          string?         CallbackMachine           { get; init; }
    public          string?         ContractVersion           { get; init; }
    public          string?         FinalState                { get; init; }
    public          string?         ReasonCode                { get; init; }
    public          string?         SourceState               { get; init; }
    public          string?         ResubmitAdvice            { get; init; }
    public required bool            CallbackFinished          { get; init; }
    public required bool            MediaConsideredDone       { get; init; }
    public required bool            AllowResubmit            { get; init; }
    public required bool            NeedsManualIntervention   { get; init; }
    public          string?         DisplayMessage            { get; init; }
    public          string?         DetailMessage             { get; init; }
    public          string?         RecommendedAction         { get; init; }
    public          string?         CorrelationId             { get; init; }
    public          DateTimeOffset? CallbackLocalTimestamp    { get; init; }
    public required int             AttemptCount              { get; init; }
    public          string?         RawResponseJson           { get; init; }
    public required DateTimeOffset  ReceivedAtUtc             { get; init; }
}
