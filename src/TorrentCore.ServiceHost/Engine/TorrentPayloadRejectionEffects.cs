using System.Text.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Engine;

public sealed class TorrentPayloadRejectionEffects : ITorrentPayloadRejectionEffects
{
    internal const string AcknowledgementPrefix = "TORRENTCORE_REJECTION_ACK:";
    private readonly Func<CancellationToken, Task<RuntimeSettingsSnapshot>> _settings;
    private readonly Func<CancellationToken, Task<IReadOnlyList<TorrentSnapshot>>> _currentTorrents;
    private readonly Func<ExternalProcessRequest, CancellationToken, Task<ExternalProcessResult>> _run;
    private readonly string _storageRoot;

    internal TorrentPayloadRejectionEffects(Func<CancellationToken, Task<RuntimeSettingsSnapshot>> settings,
        Func<CancellationToken, Task<IReadOnlyList<TorrentSnapshot>>> currentTorrents,
        Func<ExternalProcessRequest, CancellationToken, Task<ExternalProcessResult>> run, string storageRoot)
    { _settings = settings; _currentTorrents = currentTorrents; _run = run; _storageRoot = storageRoot; }

    public async Task<PayloadRejectionReply> ReportAsync(TorrentPayloadRejection rejection, CancellationToken token)
    {
        try
        {
            if (rejection.Phase != "Deleted")
            { return new(false, "Rejected payload data deletion is not confirmed; callback was not launched."); }
            var settings = await _settings(token);
            if (!settings.CompletionCallbackEnabled)
            { return new(true, "Payload deleted; downstream rejection notification is disabled by configuration.", NotRequired: true); }
            if (string.IsNullOrWhiteSpace(settings.CompletionCallbackCommandPath))
            { return new(false, "The configured rejection callback command is missing; rejection notification remains pending."); }
            var command = settings.CompletionCallbackCommandPath.Trim();
            // An older completion-only app must fail its normal environment validation, never process a completion.
            var environment = new Dictionary<string, string> { ["TR_TORRENT_NAME"] = "" };
            if (!string.IsNullOrWhiteSpace(settings.CompletionCallbackApiBaseUrlOverride))
            { environment["TVMAZE_API_COMPLETE_URL"] = settings.CompletionCallbackApiBaseUrlOverride.Trim(); }
            if (!string.IsNullOrWhiteSpace(settings.CompletionCallbackApiKeyOverride))
            { environment["TVMAZE_API_COMPLETE_API_KEY"] = settings.CompletionCallbackApiKeyOverride.Trim(); }
            var workingDirectory = settings.CompletionCallbackWorkingDirectory?.Trim();
            if (string.IsNullOrWhiteSpace(workingDirectory))
            { workingDirectory = Path.GetDirectoryName(command); }
            var invocation = new ExternalProcessRequest(command, [], TimeSpan.FromSeconds(settings.CompletionCallbackTimeoutSeconds))
            {
                ArgumentString = (settings.CompletionCallbackArguments ?? "") + " --payload-rejection",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? _storageRoot : workingDirectory,
                Environment = environment,
                StandardInput = JsonSerializer.Serialize(new
                {
                    EventId = rejection.TorrentId, Source = "TorrentCore", rejection.InfoHash,
                    rejection.OffendingFiles, rejection.RejectionReason, rejection.ObservedFiles, CleanupOutcome = "DeletedIncludingData",
                }),
            };
            var result = await _run(invocation, token);
            // Do not echo process output: configured callback arguments or diagnostics may include credentials.
            if (!result.Succeeded || result.FailureSummary is not null)
            { return new(false, $"Downstream rejection callback was not acknowledged (started={result.Started}, timedOut={result.TimedOut}, exit={result.ExitCode}); retry remains pending."); }
            var acknowledgements = result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith(AcknowledgementPrefix, StringComparison.Ordinal)).ToArray();
            if (acknowledgements.Length != 1)
            { return new(false, "Downstream callback returned no unique rejection acknowledgement; retry remains pending."); }
            var reply = JsonSerializer.Deserialize<RejectionAcknowledgement>(acknowledgements[0][AcknowledgementPrefix.Length..]);
            if (reply is null || reply.RecoveryId == Guid.Empty || !string.Equals(reply.InfoHash, rejection.InfoHash, StringComparison.OrdinalIgnoreCase))
            { return new(false, "Downstream rejection acknowledgement does not identify the submitted hash."); }
            return new(true, "Downstream callback acknowledged confirmed payload deletion.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { return new(false, "Downstream rejection acknowledgement is unconfirmed: " + ex.GetType().Name); }
    }

    public async Task DeleteAsync(TorrentPayloadRejection rejection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var candidates = ValidatePaths(rejection.DownloadRootPath, rejection.CandidatePaths);
        foreach (var other in await _currentTorrents(token))
        {
            if (other.TorrentId != rejection.TorrentId && candidates.Any(path => IsWithin(Path.GetFullPath(other.SavePath), path)))
            { throw new InvalidOperationException("Another current torrent uses the recorded cleanup path; payload retained for review."); }
        }
        // Await the existing deletion implementation; ordinary manual removal remains deferred as before.
        var error = await TorrentRemovalCleanupService.TryDeleteDataWithRetryAsync(rejection.DownloadRootPath, candidates);
        if (error is not null) { throw new IOException("Rejected payload deletion remains unconfirmed.", error); }
        if (candidates.Any(path => File.Exists(path) || Directory.Exists(path)))
        { throw new IOException("Recorded rejected payload paths remain after deletion."); }
    }

    internal static string[] ValidatePaths(string rootPath, IEnumerable<string> paths)
    {
        if (!Path.IsPathFullyQualified(rootPath)) { throw new InvalidOperationException("Recorded download root is invalid."); }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (!Path.IsPathFullyQualified(path)) { throw new InvalidOperationException("Recorded cleanup path is invalid."); }
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (full == root) { continue; } // Existing delete-data behavior never deletes the download root.
            if (!IsWithin(full, root)) { throw new InvalidOperationException("Recorded cleanup path is outside the download root."); }
            result.Add(full);
        }
        if (result.Count == 0) { throw new InvalidOperationException("No payload cleanup paths were recorded."); }
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static bool IsWithin(string path, string root) => path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    private sealed record RejectionAcknowledgement(Guid RecoveryId, string InfoHash);
}
