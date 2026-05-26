using System.Text.Json;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Application;

internal static class CompletionCallbackFeedbackMapper
{
    public static CompletionCallbackFeedbackDto Create(ReportCompletionCallbackResultRequest request,
        DateTimeOffset receivedAtUtc)
    {
        return new CompletionCallbackFeedbackDto
        {
            TorrentId = request.TorrentId,
            TorrentHash = request.TorrentHash,
            CompletionTimestamp = request.CompletionTimestamp,
            CallbackSource = request.CallbackSource,
            CallbackMachine = request.CallbackMachine,
            ContractVersion = request.ContractVersion,
            FinalState = request.FinalState,
            ReasonCode = request.ReasonCode,
            SourceState = request.SourceState,
            ResubmitAdvice = request.ResubmitAdvice,
            CallbackFinished = request.CallbackFinished,
            MediaConsideredDone = request.MediaConsideredDone,
            AllowResubmit = request.AllowResubmit,
            NeedsManualIntervention = request.NeedsManualIntervention,
            DisplayMessage = request.DisplayMessage,
            DetailMessage = request.DetailMessage,
            RecommendedAction = request.RecommendedAction,
            CorrelationId = request.CorrelationId,
            CallbackLocalTimestamp = request.CallbackLocalTimestamp,
            AttemptCount = request.AttemptCount,
            RawResponseJson = request.RawResponseJson,
            ReceivedAtUtc = receivedAtUtc,
        };
    }

    public static string Serialize(CompletionCallbackFeedbackDto feedback)
    {
        return JsonSerializer.Serialize(feedback);
    }

    public static CompletionCallbackFeedbackDto? Deserialize(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CompletionCallbackFeedbackDto>(json);
    }
}
