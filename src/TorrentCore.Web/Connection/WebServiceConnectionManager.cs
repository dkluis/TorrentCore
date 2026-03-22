#region

using TorrentCore.Client;

#endregion

namespace TorrentCore.Web.Connection;

public sealed class WebServiceConnectionManager
{
    private readonly MutableTorrentCoreEndpointProvider _endpointProvider;
    private readonly SemaphoreSlim                      _gate = new(1, 1);
    private readonly WebServiceConnectionStore          _store;
    private          bool                               _initialized;

    public WebServiceConnectionManager(WebServiceConnectionStore store,
        MutableTorrentCoreEndpointProvider endpointProvider, TorrentCoreClientOptions clientOptions)
    {
        _store            = store;
        _endpointProvider = endpointProvider;
        DefaultBaseUrl = string.IsNullOrWhiteSpace(clientOptions.BaseUrl) ? null :
                TorrentCoreClientOptions.NormalizeBaseUrl(clientOptions.BaseUrl);
    }

    public string?                           DefaultBaseUrl        { get; }
    public string?                           CurrentBaseUrl        => _endpointProvider.CurrentBaseUrl;
    public bool                              UsesPersistedOverride { get; private set; }
    public TorrentCoreConnectionProbeResult? CurrentStatus         { get; private set; }
    public event Action?                     StateChanged;

    public async Task<TorrentCoreConnectionProbeResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return CurrentStatus ?? await ProbeAndApplyAsync(CurrentBaseUrl, false, cancellationToken);
    }

    public async Task<TorrentCoreConnectionProbeResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await ProbeAndApplyAsync(CurrentBaseUrl, true, cancellationToken);
    }

    public Task<TorrentCoreConnectionProbeResult> TestAsync(string? baseUrl,
        CancellationToken                                           cancellationToken = default)
    {
        return TorrentCoreConnectionProbe.CheckAsync(baseUrl, cancellationToken);
    }

    public async Task<TorrentCoreConnectionProbeResult> SaveAsync(string? baseUrl,
        CancellationToken                                                 cancellationToken = default)
    {
        var probeResult = await TorrentCoreConnectionProbe.CheckAsync(baseUrl, cancellationToken);
        if (!probeResult.IsReachable || string.IsNullOrWhiteSpace(probeResult.BaseUrl))
        {
            return probeResult;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _store.SaveAsync(
                new WebServiceConnectionRecord
                {
                    BaseUrl      = probeResult.BaseUrl,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                }, cancellationToken
            );

            UsesPersistedOverride = true;
            _initialized          = true;
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

            var initialBaseUrl = persistedRecord?.BaseUrl ?? DefaultBaseUrl;
            _endpointProvider.Update(initialBaseUrl);
            CurrentStatus = await TorrentCoreConnectionProbe.CheckAsync(initialBaseUrl, cancellationToken);
            _initialized  = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TorrentCoreConnectionProbeResult> ProbeAndApplyAsync(string? baseUrl, bool raiseChangedEvent,
        CancellationToken                                                           cancellationToken)
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
