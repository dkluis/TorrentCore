using TorrentCore.Client;

namespace TorrentCore.Avalonia.Infrastructure;

public sealed class AvaloniaServiceConnectionManager
{
    private readonly AppConnectionSettingsStore _store;
    private readonly MutableTorrentCoreEndpointProvider _endpointProvider;
    private readonly string? _defaultBaseUrl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public AvaloniaServiceConnectionManager(
        AppConnectionSettingsStore store,
        MutableTorrentCoreEndpointProvider endpointProvider,
        TorrentCoreClientOptions clientOptions)
    {
        _store = store;
        _endpointProvider = endpointProvider;
        _defaultBaseUrl = string.IsNullOrWhiteSpace(clientOptions.BaseUrl)
            ? null
            : TorrentCoreClientOptions.NormalizeBaseUrl(clientOptions.BaseUrl);
    }

    public string? DefaultBaseUrl => _defaultBaseUrl;

    public string? CurrentBaseUrl => _endpointProvider.CurrentBaseUrl;

    public TorrentCoreConnectionProbeResult? CurrentStatus { get; private set; }

    public async Task<TorrentCoreConnectionProbeResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return CurrentStatus ?? await RefreshAsync(cancellationToken);
    }

    public async Task<TorrentCoreConnectionProbeResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        CurrentStatus = await TorrentCoreConnectionProbe.CheckAsync(CurrentBaseUrl, cancellationToken);
        return CurrentStatus;
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
            await _store.SaveAsync(new AppConnectionSettingsRecord
            {
                BaseUrl = probeResult.BaseUrl,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }, cancellationToken);

            _initialized = true;
            _endpointProvider.Update(probeResult.BaseUrl);
            CurrentStatus = probeResult;
        }
        finally
        {
            _gate.Release();
        }

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
}
