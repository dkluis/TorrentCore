using System.Text.Json;
using TorrentCore.Contracts;

namespace TorrentCore.Client;

public static class TorrentCoreConnectionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private const string ExpectedServiceName = "TorrentCore.Service";

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

            return await ValidateHealthResponseAsync(response, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (false, exception.Message);
        }
    }

    private static async Task<(bool IsReachable, string? ErrorMessage)> ValidateHealthResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, "TorrentCore returned an invalid health response.");
            }

            var root = document.RootElement;
            var serviceName = ReadOptionalString(root, "serviceName");
            if (!string.Equals(serviceName, ExpectedServiceName, StringComparison.Ordinal))
            {
                var nameDetail = string.IsNullOrWhiteSpace(serviceName) ? string.Empty : $" ({serviceName})";
                return (false, $"The address responded, but it is not a TorrentCore service{nameDetail}.");
            }

            if (!root.TryGetProperty("apiVersion", out var apiVersionElement) ||
                apiVersionElement.ValueKind == JsonValueKind.Null)
            {
                return (true, null);
            }

            if (apiVersionElement.ValueKind != JsonValueKind.Number || !apiVersionElement.TryGetInt32(out var apiVersion))
            {
                return (false, "TorrentCore returned an invalid API version in its health response.");
            }

            return apiVersion > ServiceApiContract.CurrentVersion
                ? (false, $"This TorrentCore service uses unsupported API version {apiVersion}.")
                : (true, null);
        }
        catch (JsonException exception)
        {
            return (false, $"TorrentCore returned an invalid health response: {exception.Message}");
        }
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
