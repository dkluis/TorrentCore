using System.Net;
using System.Net.Http.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

public sealed class TorrentPayloadRejectionEffects : ITorrentPayloadRejectionEffects
{
    public const string HttpClientName = "TvmazePayloadRejection";
    private readonly IHttpClientFactory _clients;
    private readonly Func<CancellationToken, Task<(string? Url, string? Key)>> _connection;
    private readonly Func<CancellationToken, Task<IReadOnlyList<TorrentSnapshot>>> _currentTorrents;

    public TorrentPayloadRejectionEffects(IHttpClientFactory clients, IRuntimeSettingsService settings, ITorrentStateStore states)
        : this(clients, async token =>
        {
            var value = await settings.GetEffectiveSettingsAsync(token);
            return (value.CompletionCallbackApiBaseUrlOverride, value.CompletionCallbackApiKeyOverride);
        }, states.ListAsync) { }
    internal TorrentPayloadRejectionEffects(IHttpClientFactory clients,
        Func<CancellationToken, Task<(string? Url, string? Key)>> connection,
        Func<CancellationToken, Task<IReadOnlyList<TorrentSnapshot>>> currentTorrents)
    { _clients = clients; _connection = connection; _currentTorrents = currentTorrents; }

    public async Task<PayloadRejectionReply> ReportAsync(TorrentPayloadRejection rejection, bool dataDeleted, CancellationToken token)
    {
        try
        {
            var current = await _connection(token);
            if (!Uri.TryCreate(current.Url?.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(baseUri.UserInfo) ||
                string.IsNullOrWhiteSpace(current.Key))
            { return new(false, Message: "TVMaze API URL/key is missing or invalid; executable torrent remains blocked."); }
            using var client = _clients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/acquisition/rejections"));
            request.Headers.Add("X-API-Key", current.Key);
            request.Content = JsonContent.Create(new
            {
                EventId = rejection.TorrentId, Source = "TorrentCore", rejection.InfoHash, rejection.OffendingFiles,
                // Existing contract: Failed means cleanup is unconfirmed and recovery must wait.
                CleanupOutcome = dataDeleted ? "DeletedIncludingData" : "Failed",
            });
            using var response = await client.SendAsync(request, token);
            if (response.StatusCode != HttpStatusCode.Accepted)
            { return new(false, Message: $"TVMaze rejection registration was not acknowledged (HTTP {(int)response.StatusCode}); no alternate/reset authorized."); }
            var reply = await response.Content.ReadFromJsonAsync<RejectionAcknowledgement>(token);
            if (reply is null || reply.RecoveryId == Guid.Empty || !string.Equals(reply.InfoHash, rejection.InfoHash, StringComparison.OrdinalIgnoreCase))
            { return new(false, Message: "TVMaze rejection acknowledgement does not identify the submitted hash."); }
            return new(true, reply.Outcome == "SeasonExcluded", reply.Outcome == "SeasonExcluded"
                ? "Recorded season pack excluded; download retained."
                : dataDeleted ? "TVMaze acknowledged confirmed deletion; linked recovery/reporting owns the next action."
                : "TVMaze registered rejection with cleanup unconfirmed; no alternate/reset can start yet.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        { return new(false, Message: "TVMaze rejection acknowledgement is unconfirmed: " + ex.GetType().Name); }
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
        if (error is not null) { throw new IOException("Executable payload deletion remains unconfirmed.", error); }
        if (candidates.Any(path => File.Exists(path) || Directory.Exists(path)))
        { throw new IOException("Recorded executable payload paths remain after deletion."); }
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
    private sealed record RejectionAcknowledgement(Guid RecoveryId, string InfoHash, string Outcome);
}
