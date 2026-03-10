using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Client;

public sealed class TorrentCoreClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/health", cancellationToken);
        return await ReadResponseAsync<ServiceHealthDto>(response, cancellationToken);
    }

    public async Task<EngineHostStatusDto?> GetHostStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/host/status", cancellationToken);
        return await ReadResponseAsync<EngineHostStatusDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/torrents", cancellationToken);
        return await ReadResponseAsync<IReadOnlyList<TorrentSummaryDto>>(response, cancellationToken)
               ?? Array.Empty<TorrentSummaryDto>();
    }

    public async Task<TorrentDetailDto?> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/torrents/{torrentId}", cancellationToken);
        return await ReadResponseAsync<TorrentDetailDto>(response, cancellationToken);
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/torrents", request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TorrentDetailDto>(response, cancellationToken)
               ?? throw new InvalidOperationException("TorrentCore service returned no torrent payload.");
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/torrents/{torrentId}/pause", content: null, cancellationToken);
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken)
               ?? throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/torrents/{torrentId}/resume", content: null, cancellationToken);
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken)
               ?? throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/torrents/{torrentId}/remove", request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken)
               ?? throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    private static async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var serviceError = await ReadServiceErrorAsync(response, cancellationToken);
        var message = serviceError?.Message ?? $"TorrentCore request failed with status code {(int)response.StatusCode}.";
        throw new TorrentCoreClientException(message, (int)response.StatusCode, serviceError);
    }

    private static async Task<ServiceErrorDto?> ReadServiceErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is 0)
        {
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonNode.ParseAsync(contentStream, cancellationToken: cancellationToken);

        if (payload is null)
        {
            return null;
        }

        return new ServiceErrorDto
        {
            Code    = payload["code"]?.GetValue<string>() ?? "request_failed",
            Message = payload["message"]?.GetValue<string>() ?? payload["detail"]?.GetValue<string>() ?? "TorrentCore request failed.",
            Target  = payload["target"]?.GetValue<string>(),
            TraceId = payload["traceId"]?.GetValue<string>(),
        };
    }
}
