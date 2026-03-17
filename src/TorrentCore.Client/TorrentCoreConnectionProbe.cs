namespace TorrentCore.Client;

public static class TorrentCoreConnectionProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    public static async Task<TorrentCoreConnectionProbeResult> CheckAsync(
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new TorrentCoreConnectionProbeResult
            {
                BaseUrl = null,
                IsConfigured = false,
                IsReachable = false,
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
                BaseUrl = baseUrl.Trim(),
                IsConfigured = true,
                IsReachable = false,
                ErrorMessage = exception.Message,
                CheckedAtUtc = checkedAtUtc,
            };
        }

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
                return new TorrentCoreConnectionProbeResult
                {
                    BaseUrl = baseUri.ToString(),
                    IsConfigured = true,
                    IsReachable = false,
                    ErrorMessage = $"The service returned HTTP {(int)response.StatusCode}.",
                    CheckedAtUtc = checkedAtUtc,
                };
            }

            return new TorrentCoreConnectionProbeResult
            {
                BaseUrl = baseUri.ToString(),
                IsConfigured = true,
                IsReachable = true,
                ErrorMessage = null,
                CheckedAtUtc = checkedAtUtc,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new TorrentCoreConnectionProbeResult
            {
                BaseUrl = baseUri.ToString(),
                IsConfigured = true,
                IsReachable = false,
                ErrorMessage = exception.Message,
                CheckedAtUtc = checkedAtUtc,
            };
        }
    }
}
