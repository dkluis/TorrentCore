namespace TorrentCore.Service.Engine;

internal sealed class TorrentMetadataRecoveryState
{
    private readonly object _gate = new();
    private DateTimeOffset? _resolvingSinceUtc;
    private DateTimeOffset? _lastDiscoveryActivityAtUtc;
    private DateTimeOffset? _lastRefreshAtUtc;
    private DateTimeOffset? _lastRestartAtUtc;
    private DateTimeOffset? _lastResetAtUtc;

    public void Observe(DateTimeOffset now, bool isResolvingMetadata, bool hasMetadata, int openConnections)
    {
        lock (_gate)
        {
            if (!isResolvingMetadata || hasMetadata)
            {
                ResetUnsafe();
                return;
            }

            _resolvingSinceUtc ??= now;

            if (openConnections > 0)
            {
                _lastDiscoveryActivityAtUtc = now;
            }
        }
    }

    public void NoteDiscoveryActivity(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastDiscoveryActivityAtUtc = now;
        }
    }

    public TorrentMetadataRecoveryDecision Evaluate(DateTimeOffset now, int staleSeconds, int restartDelaySeconds)
    {
        lock (_gate)
        {
            if (_resolvingSinceUtc is null)
            {
                return TorrentMetadataRecoveryDecision.None;
            }

            var staleSinceUtc = _lastDiscoveryActivityAtUtc ?? _resolvingSinceUtc.Value;
            if (now - staleSinceUtc < TimeSpan.FromSeconds(staleSeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.None,
                    _resolvingSinceUtc,
                    _lastDiscoveryActivityAtUtc,
                    _lastRefreshAtUtc,
                    _lastRestartAtUtc,
                    _lastResetAtUtc,
                    staleSinceUtc);
            }

            if (_lastRefreshAtUtc is null || _lastRefreshAtUtc < staleSinceUtc)
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Refresh,
                    _resolvingSinceUtc,
                    _lastDiscoveryActivityAtUtc,
                    _lastRefreshAtUtc,
                    _lastRestartAtUtc,
                    _lastResetAtUtc,
                    staleSinceUtc);
            }

            if ((_lastRestartAtUtc is null || _lastRestartAtUtc < _lastRefreshAtUtc) &&
                now - _lastRefreshAtUtc.Value >= TimeSpan.FromSeconds(restartDelaySeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Restart,
                    _resolvingSinceUtc,
                    _lastDiscoveryActivityAtUtc,
                    _lastRefreshAtUtc,
                    _lastRestartAtUtc,
                    _lastResetAtUtc,
                    staleSinceUtc);
            }

            if ((_lastResetAtUtc is null || (_lastRestartAtUtc is not null && _lastResetAtUtc < _lastRestartAtUtc)) &&
                _lastRestartAtUtc is not null &&
                now - _lastRestartAtUtc.Value >= TimeSpan.FromSeconds(restartDelaySeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Reset,
                    _resolvingSinceUtc,
                    _lastDiscoveryActivityAtUtc,
                    _lastRefreshAtUtc,
                    _lastRestartAtUtc,
                    _lastResetAtUtc,
                    staleSinceUtc);
            }

            return new TorrentMetadataRecoveryDecision(
                MetadataRecoveryAction.None,
                _resolvingSinceUtc,
                _lastDiscoveryActivityAtUtc,
                _lastRefreshAtUtc,
                _lastRestartAtUtc,
                _lastResetAtUtc,
                staleSinceUtc);
        }
    }

    public void MarkRefresh(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastRefreshAtUtc = now;
        }
    }

    public void MarkRestart(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastRestartAtUtc = now;
            _lastRefreshAtUtc = now;
        }
    }

    public void MarkReset(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastResetAtUtc = now;
            _lastRestartAtUtc = now;
            _lastRefreshAtUtc = now;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            ResetUnsafe();
        }
    }

    private void ResetUnsafe()
    {
        _resolvingSinceUtc = null;
        _lastDiscoveryActivityAtUtc = null;
        _lastRefreshAtUtc = null;
        _lastRestartAtUtc = null;
        _lastResetAtUtc = null;
    }
}

internal enum MetadataRecoveryAction
{
    None = 0,
    Refresh = 1,
    Restart = 2,
    Reset = 3,
}

internal readonly record struct TorrentMetadataRecoveryDecision(
    MetadataRecoveryAction Action,
    DateTimeOffset? ResolvingSinceUtc,
    DateTimeOffset? LastDiscoveryActivityAtUtc,
    DateTimeOffset? LastRefreshAtUtc,
    DateTimeOffset? LastRestartAtUtc,
    DateTimeOffset? LastResetAtUtc,
    DateTimeOffset StaleSinceUtc)
{
    public static TorrentMetadataRecoveryDecision None =>
        new(
            MetadataRecoveryAction.None,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.MinValue);
}
