using System.Net.Http.Json;
using TorrentCore.Contracts.Health;

namespace TorrentCore.Client;

public sealed class TorrentCoreClient(HttpClient httpClient)
{
    public async Task<ServiceHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<ServiceHealthDto>("api/health", cancellationToken);
    }
}
