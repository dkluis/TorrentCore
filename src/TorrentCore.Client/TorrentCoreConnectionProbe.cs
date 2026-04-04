namespace TorrentCore.Client;

public static class TorrentCoreConnectionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public static async Task<TorrentCoreConnectionProbeResult> CheckAsync(string? baseUrl,
        CancellationToken                                                         cancellationToken = default)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new TorrentCoreConnectionProbeResult
            {
                BaseUrl      = null,
                IsConfigured = false,
                IsReachable  = false,
                ErrorMessage = "No TorrentCore service endpoint is configured.",
                CheckedAtUtc = checkedAtUtc,
            };
        }

        Uri baseUri;
        try
        {
            baseUri = TorrentCoreClientOptions.ParseBaseUrl(baseUrl);
        }
        catch (Exception exception)
        {
            return new TorrentCoreConnectionProbeResult
            {
                BaseUrl      = baseUrl.Trim(),
                IsConfigured = true,
                IsReachable  = false,
                ErrorMessage = exception.Message,
                CheckedAtUtc = checkedAtUtc,
            };
        }

        var candidates = BuildCandidates(baseUri);
        string? lastError = null;

        foreach (var candidate in candidates)
        {
            var candidateResult = await ProbeCandidateAsync(candidate, cancellationToken);
            if (candidateResult.IsReachable)
            {
                return new TorrentCoreConnectionProbeResult
                {
                    BaseUrl = candidate.ToString(),
                    IsConfigured = true,
                    IsReachable = true,
                    ErrorMessage = null,
                    CheckedAtUtc = checkedAtUtc,
                };
            }

            lastError = candidateResult.ErrorMessage;
        }

        return new TorrentCoreConnectionProbeResult
        {
            BaseUrl = baseUri.ToString(),
            IsConfigured = true,
            IsReachable = false,
            ErrorMessage = lastError ?? "Unable to reach the configured endpoint.",
            CheckedAtUtc = checkedAtUtc,
        };
    }

    private static IReadOnlyList<Uri> BuildCandidates(Uri baseUri)
    {
        var candidates = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidate(baseUri);

        if (string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            var localhostCandidate = new UriBuilder(baseUri) { Host = "localhost" }.Uri;
            AddCandidate(localhostCandidate);
        }

        return candidates;

        void AddCandidate(Uri candidate)
        {
            var key = candidate.ToString();
            if (seen.Add(key))
            {
                candidates.Add(candidate);
            }
        }
    }

    private static async Task<(bool IsReachable, string? ErrorMessage)> ProbeCandidateAsync(
        Uri baseUri,
        CancellationToken cancellationToken
    )
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = baseUri,
            Timeout = DefaultTimeout,
        };

        try
        {
            using var response = await httpClient.GetAsync("api/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"The service returned HTTP {(int)response.StatusCode}.");
            }

            return (true, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (false, exception.Message);
        }
    }
}
