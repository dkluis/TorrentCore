namespace TorrentCore.Client;

public sealed class MutableTorrentCoreEndpointProvider : ITorrentCoreEndpointProvider
{
    private readonly object _gate = new();
    private string? _currentBaseUrl;
    private Uri? _currentBaseUri;

    public MutableTorrentCoreEndpointProvider(string? initialBaseUrl = null)
    {
        Update(initialBaseUrl);
    }

    public string? CurrentBaseUrl
    {
        get
        {
            lock (_gate)
            {
                return _currentBaseUrl;
            }
        }
    }

    public Uri? CurrentBaseUri
    {
        get
        {
            lock (_gate)
            {
                return _currentBaseUri;
            }
        }
    }

    public void Update(string? baseUrl)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _currentBaseUrl = null;
                _currentBaseUri = null;
                return;
            }

            var normalizedBaseUrl = TorrentCoreClientOptions.NormalizeBaseUrl(baseUrl);
            _currentBaseUrl = normalizedBaseUrl;
            _currentBaseUri = TorrentCoreClientOptions.ParseBaseUrl(normalizedBaseUrl);
        }
    }
}
