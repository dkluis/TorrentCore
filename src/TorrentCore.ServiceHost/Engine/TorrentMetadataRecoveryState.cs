namespace TorrentCore.Service.Engine;

internal sealed class TorrentMetadataRecoveryState
{
    private const int MaxBackoffMultiplier = 8;

    private readonly object          _gate = new();
    private          DateTimeOffset? _lastDiscoveryActivityAtUtc;
    private          DateTimeOffset? _lastRefreshAtUtc;
    private          DateTimeOffset? _lastResetAtUtc;
    private          DateTimeOffset? _lastRestartAtUtc;
    private          int             _recoveryCycle;
    private          DateTimeOffset? _resolvingSinceUtc;

    public void Observe(DateTimeOffset now, bool isResolvingMetadata, bool hasMetadata)
    {
        lock (_gate)
        {
            if (!isResolvingMetadata || hasMetadata)
            {
                ResetUnsafe();
                return;
            }

            _resolvingSinceUtc ??= now;
        }
    }

    public void NoteDiscoveryActivity(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc          ??= now;
            _lastDiscoveryActivityAtUtc =   now;
            _recoveryCycle              =   0;
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
            var backoffMultiplier = GetBackoffMultiplier(_recoveryCycle);
            var effectiveStaleSeconds = staleSeconds * backoffMultiplier;
            var effectiveRestartDelaySeconds = restartDelaySeconds * backoffMultiplier;
            if (now - staleSinceUtc < TimeSpan.FromSeconds(effectiveStaleSeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.None, _resolvingSinceUtc, _lastDiscoveryActivityAtUtc, _lastRefreshAtUtc,
                    _lastRestartAtUtc, _lastResetAtUtc, staleSinceUtc, _recoveryCycle, backoffMultiplier,
                    effectiveStaleSeconds, effectiveRestartDelaySeconds
                );
            }

            if (_lastRefreshAtUtc is null || _lastRefreshAtUtc < staleSinceUtc)
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Refresh, _resolvingSinceUtc, _lastDiscoveryActivityAtUtc, _lastRefreshAtUtc,
                    _lastRestartAtUtc, _lastResetAtUtc, staleSinceUtc, _recoveryCycle, backoffMultiplier,
                    effectiveStaleSeconds, effectiveRestartDelaySeconds
                );
            }

            if ((_lastRestartAtUtc is null || _lastRestartAtUtc < _lastRefreshAtUtc) &&
                now - _lastRefreshAtUtc.Value >= TimeSpan.FromSeconds(effectiveRestartDelaySeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Restart, _resolvingSinceUtc, _lastDiscoveryActivityAtUtc, _lastRefreshAtUtc,
                    _lastRestartAtUtc, _lastResetAtUtc, staleSinceUtc, _recoveryCycle, backoffMultiplier,
                    effectiveStaleSeconds, effectiveRestartDelaySeconds
                );
            }

            if ((_lastResetAtUtc is null || (_lastRestartAtUtc is not null && _lastResetAtUtc < _lastRestartAtUtc)) &&
                _lastRestartAtUtc is not null                                                                       &&
                now - _lastRestartAtUtc.Value >= TimeSpan.FromSeconds(effectiveRestartDelaySeconds))
            {
                return new TorrentMetadataRecoveryDecision(
                    MetadataRecoveryAction.Reset, _resolvingSinceUtc, _lastDiscoveryActivityAtUtc, _lastRefreshAtUtc,
                    _lastRestartAtUtc, _lastResetAtUtc, staleSinceUtc, _recoveryCycle, backoffMultiplier,
                    effectiveStaleSeconds, effectiveRestartDelaySeconds
                );
            }

            return new TorrentMetadataRecoveryDecision(
                MetadataRecoveryAction.None, _resolvingSinceUtc, _lastDiscoveryActivityAtUtc, _lastRefreshAtUtc,
                _lastRestartAtUtc, _lastResetAtUtc, staleSinceUtc, _recoveryCycle, backoffMultiplier,
                effectiveStaleSeconds, effectiveRestartDelaySeconds
            );
        }
    }

    public void MarkRefresh(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastRefreshAtUtc  =   now;
        }
    }

    public void MarkRestart(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc ??= now;
            _lastRestartAtUtc  =   now;
            _lastRefreshAtUtc  =   now;
        }
    }

    public void MarkReset(DateTimeOffset now)
    {
        lock (_gate)
        {
            _resolvingSinceUtc          = now;
            _lastDiscoveryActivityAtUtc = null;
            _lastResetAtUtc             = now;
            _lastRestartAtUtc           = null;
            _lastRefreshAtUtc           = now;
            _recoveryCycle++;
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
        _resolvingSinceUtc          = null;
        _lastDiscoveryActivityAtUtc = null;
        _lastRefreshAtUtc           = null;
        _lastRestartAtUtc           = null;
        _lastResetAtUtc             = null;
        _recoveryCycle              = 0;
    }

    private static int GetBackoffMultiplier(int recoveryCycle)
        => Math.Min(1 << Math.Min(recoveryCycle, 3), MaxBackoffMultiplier);
}
internal enum MetadataRecoveryAction
{
    None    = 0,
    Refresh = 1,
    Restart = 2,
    Reset   = 3,
}
internal readonly record struct TorrentMetadataRecoveryDecision(MetadataRecoveryAction Action,
    DateTimeOffset? ResolvingSinceUtc, DateTimeOffset? LastDiscoveryActivityAtUtc, DateTimeOffset? LastRefreshAtUtc,
    DateTimeOffset? LastRestartAtUtc, DateTimeOffset? LastResetAtUtc, DateTimeOffset StaleSinceUtc, int RecoveryCycle,
    int BackoffMultiplier, int EffectiveStaleSeconds, int EffectiveRestartDelaySeconds)
{
    public static TorrentMetadataRecoveryDecision None
        => new(
            MetadataRecoveryAction.None, null, null, null, null,
            null, DateTimeOffset.MinValue, 0, 1, 0, 0
        );
}
