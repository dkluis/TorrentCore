#region

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

#endregion

namespace TorrentCore.Client;

public sealed class TorrentCoreClient(HttpClient httpClient, ITorrentCoreEndpointProvider endpointProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/health"), cancellationToken);
        return await ReadResponseAsync<ServiceHealthDto>(response, cancellationToken);
    }

    public async Task<EngineHostStatusDto?> GetHostStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/host/status"), cancellationToken);
        return await ReadResponseAsync<EngineHostStatusDto>(response, cancellationToken);
    }

    public async Task<DashboardLifecycleSummaryDto?> GetDashboardLifecycleAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/host/dashboard-lifecycle"), cancellationToken);
        return await ReadResponseAsync<DashboardLifecycleSummaryDto>(response, cancellationToken);
    }

    public async Task<RuntimeSettingsDto?> GetRuntimeSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/host/runtime-settings"), cancellationToken);
        return await ReadResponseAsync<RuntimeSettingsDto>(response, cancellationToken);
    }

    public async Task<RuntimeSettingsDto> UpdateRuntimeSettingsAsync(UpdateRuntimeSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            BuildRequestUri("api/host/runtime-settings"), request, JsonOptions, cancellationToken
        );
        return await ReadResponseAsync<RuntimeSettingsDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no runtime settings payload.");
    }

    public async Task<ServiceRestartRequestResultDto> RequestServiceRestartAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri("api/host/restart-service"), null, cancellationToken
        );
        return await ReadResponseAsync<ServiceRestartRequestResultDto>(response, cancellationToken) ??
               throw new InvalidOperationException("TorrentCore service returned no restart payload.");
    }

    public async Task<IReadOnlyList<TorrentCategoryDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/categories"), cancellationToken);
        return await ReadResponseAsync<IReadOnlyList<TorrentCategoryDto>>(response, cancellationToken) ??
                Array.Empty<TorrentCategoryDto>();
    }

    public async Task<TorrentCategoryDto> UpdateCategoryAsync(string key, UpdateTorrentCategoryRequest request,
        CancellationToken                                            cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            BuildRequestUri($"api/categories/{Uri.EscapeDataString(key)}"), request, JsonOptions, cancellationToken
        );
        return await ReadResponseAsync<TorrentCategoryDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no category payload.");
    }

    public async Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri("api/torrents"), cancellationToken);
        return await ReadResponseAsync<IReadOnlyList<TorrentSummaryDto>>(response, cancellationToken) ??
                Array.Empty<TorrentSummaryDto>();
    }

    public async Task<IReadOnlyList<ActivityLogEntryDto>> GetRecentLogsAsync(int take = 100, string? category = null,
        string? eventType = null, string? level = null, Guid? torrentId = null, Guid? serviceInstanceId = null,
        DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string> {$"take={take}"};

        if (!string.IsNullOrWhiteSpace(category))
        {
            query.Add($"category={Uri.EscapeDataString(category)}");
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query.Add($"eventType={Uri.EscapeDataString(eventType)}");
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            query.Add($"level={Uri.EscapeDataString(level)}");
        }

        if (torrentId is not null)
        {
            query.Add($"torrentId={torrentId.Value:D}");
        }

        if (serviceInstanceId is not null)
        {
            query.Add($"serviceInstanceId={serviceInstanceId.Value:D}");
        }

        if (fromUtc is not null)
        {
            query.Add($"fromUtc={Uri.EscapeDataString(fromUtc.Value.ToString("O"))}");
        }

        if (toUtc is not null)
        {
            query.Add($"toUtc={Uri.EscapeDataString(toUtc.Value.ToString("O"))}");
        }

        using var response = await httpClient.GetAsync(
            BuildRequestUri($"api/logs?{string.Join("&", query)}"), cancellationToken
        );
        return await ReadResponseAsync<IReadOnlyList<ActivityLogEntryDto>>(response, cancellationToken) ??
                Array.Empty<ActivityLogEntryDto>();
    }

    public async Task<DeleteOrphanedTorrentLogsResultDto> DeleteOrphanedTorrentLogsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri("api/logs/delete-orphaned-torrent-logs"), null, cancellationToken
        );
        return await ReadResponseAsync<DeleteOrphanedTorrentLogsResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no log cleanup payload.");
    }

    public async Task<TorrentDetailDto?> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BuildRequestUri($"api/torrents/{torrentId}"), cancellationToken);
        return await ReadResponseAsync<TorrentDetailDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TorrentPeerDto>> GetTorrentPeersAsync(Guid torrentId,
        CancellationToken                                                     cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            BuildRequestUri($"api/torrents/{torrentId}/peers"), cancellationToken
        );
        return await ReadResponseAsync<IReadOnlyList<TorrentPeerDto>>(response, cancellationToken) ??
                Array.Empty<TorrentPeerDto>();
    }

    public async Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId,
        CancellationToken                                                        cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            BuildRequestUri($"api/torrents/{torrentId}/trackers"), cancellationToken
        );
        return await ReadResponseAsync<IReadOnlyList<TorrentTrackerDto>>(response, cancellationToken) ??
                Array.Empty<TorrentTrackerDto>();
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request,
        CancellationToken                                               cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            BuildRequestUri("api/torrents"), request, JsonOptions, cancellationToken
        );
        return await ReadResponseAsync<TorrentDetailDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no torrent payload.");
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri($"api/torrents/{torrentId}/pause"), null, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri($"api/torrents/{torrentId}/resume"), null, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> RefreshMetadataAsync(Guid torrentId,
        CancellationToken                                               cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri($"api/torrents/{torrentId}/metadata/refresh"), null, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId,
        CancellationToken                                                    cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri($"api/torrents/{torrentId}/metadata/reset"), null, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId,
        CancellationToken                                                       cancellationToken = default)
    {
        using var response = await httpClient.PostAsync(
            BuildRequestUri($"api/torrents/{torrentId}/completion-callback/retry"), null, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
        CancellationToken                                      cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            BuildRequestUri($"api/torrents/{torrentId}/remove"), request, JsonOptions, cancellationToken
        );
        return await ReadResponseAsync<TorrentActionResultDto>(response, cancellationToken) ??
                throw new InvalidOperationException("TorrentCore service returned no action payload.");
    }

    private Uri BuildRequestUri(string relativePath)
    {
        var baseUri = endpointProvider.CurrentBaseUri;
        if (baseUri is null)
        {
            throw new InvalidOperationException("TorrentCore service endpoint is not configured.");
        }

        return new Uri(baseUri, relativePath);
    }

    private static async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response,
        CancellationToken                                                  cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        var serviceError = await ReadServiceErrorAsync(response, cancellationToken);
        var message = serviceError?.Message ??
                $"TorrentCore request failed with status code {(int) response.StatusCode}.";
        throw new TorrentCoreClientException(message, (int) response.StatusCode, serviceError);
    }

    private static async Task<ServiceErrorDto?> ReadServiceErrorAsync(HttpResponseMessage response,
        CancellationToken                                                                 cancellationToken)
    {
        if (response.Content.Headers.ContentLength is 0)
        {
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var             payload       = await JsonNode.ParseAsync(contentStream, cancellationToken: cancellationToken);

        if (payload is null)
        {
            return null;
        }

        return new ServiceErrorDto
        {
            Code = payload["code"]?.GetValue<string>() ?? "request_failed",
            Message = payload["message"]?.GetValue<string>() ??
                    payload["detail"]?.GetValue<string>() ?? "TorrentCore request failed.",
            Target  = payload["target"]?.GetValue<string>(),
            TraceId = payload["traceId"]?.GetValue<string>(),
        };
    }
}
