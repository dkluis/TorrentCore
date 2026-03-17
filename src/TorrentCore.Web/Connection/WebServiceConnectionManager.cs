using TorrentCore.Client;

namespace TorrentCore.Web.Connection;

public sealed class WebServiceConnectionManager
{
    private readonly WebServiceConnectionStore _store;
    private readonly MutableTorrentCoreEndpointProvider _endpointProvider;
    private readonly string? _defaultBaseUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public WebServiceConnectionManager(
        WebServiceConnectionStore store,
        MutableTorrentCoreEndpointProvider endpointProvider,
        TorrentCoreClientOptions clientOptions)
    {
        _store = store;
        _endpointProvider = endpointProvider;
        _defaultBaseUrl = string.IsNullOrWhiteSpace(clientOptions.BaseUrl)
            ? null
            : TorrentCoreClientOptions.NormalizeBaseUrl(clientOptions.BaseUrl);
    }

    public event Action? StateChanged;

    public string? DefaultBaseUrl => _defaultBaseUrl;

    public string? CurrentBaseUrl => _endpointProvider.CurrentBaseUrl;

    public bool UsesPersistedOverride { get; private set; }

    public TorrentCoreConnectionProbeResult? CurrentStatus { get; private set; }

    public async Task<TorrentCoreConnectionProbeResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return CurrentStatus ?? await ProbeAndApplyAsync(CurrentBaseUrl, raiseChangedEvent: false, cancellationToken);
    }

    public async Task<TorrentCoreConnectionProbeResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await ProbeAndApplyAsync(CurrentBaseUrl, raiseChangedEvent: true, cancellationToken);
    }

    public Task<TorrentCoreConnectionProbeResult> TestAsync(string? baseUrl, CancellationToken cancellationToken = default) =>
        TorrentCoreConnectionProbe.CheckAsync(baseUrl, cancellationToken);

    public async Task<TorrentCoreConnectionProbeResult> SaveAsync(string? baseUrl, CancellationToken cancellationToken = default)
    {
        var probeResult = await TorrentCoreConnectionProbe.CheckAsync(baseUrl, cancellationToken);
        if (!probeResult.IsReachable || string.IsNullOrWhiteSpace(probeResult.BaseUrl))
        {
            return probeResult;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveAsync(new WebServiceConnectionRecord
            {
                BaseUrl = probeResult.BaseUrl,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            UsesPersistedOverride = true;
            _initialized = true;
            _endpointProvider.Update(probeResult.BaseUrl);
            CurrentStatus = probeResult;
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke();
        return probeResult;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            var persistedRecord = await _store.LoadAsync(cancellationToken);
            UsesPersistedOverride = !string.IsNullOrWhiteSpace(persistedRecord?.BaseUrl);

            var initialBaseUrl = persistedRecord?.BaseUrl ?? _defaultBaseUrl;
            _endpointProvider.Update(initialBaseUrl);
            CurrentStatus = await TorrentCoreConnectionProbe.CheckAsync(initialBaseUrl, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TorrentCoreConnectionProbeResult> ProbeAndApplyAsync(
        string? baseUrl,
        bool raiseChangedEvent,
        CancellationToken cancellationToken)
    {
        var probeResult = await TorrentCoreConnectionProbe.CheckAsync(baseUrl, cancellationToken);
        CurrentStatus = probeResult;

        if (raiseChangedEvent)
        {
            StateChanged?.Invoke();
        }

        return probeResult;
    }
}
