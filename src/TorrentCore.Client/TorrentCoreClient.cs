using System.Net.Http.Json;
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
        return await httpClient.GetFromJsonAsync<ServiceHealthDto>("api/health", JsonOptions, cancellationToken);
    }

    public async Task<EngineHostStatusDto?> GetHostStatusAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status", JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents", JsonOptions, cancellationToken)
               ?? Array.Empty<TorrentSummaryDto>();
    }

    public async Task<TorrentDetailDto?> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}", JsonOptions, cancellationToken);
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

        var serviceError = await response.Content.ReadFromJsonAsync<ServiceErrorDto>(JsonOptions, cancellationToken);
        var message = serviceError?.Message ?? $"TorrentCore request failed with status code {(int)response.StatusCode}.";
        throw new TorrentCoreClientException(message, (int)response.StatusCode, serviceError);
    }
}
