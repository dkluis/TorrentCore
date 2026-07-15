namespace TorrentCore.Service.Engine;

internal sealed class TorrentDownloadRecoveryState
{
    private const int MaxBackoffMultiplier = 8;

    private readonly object          _gate = new();
    private          DateTimeOffset? _downloadingSinceUtc;
    private          DateTimeOffset? _lastActionAtUtc;
    private          DownloadRecoveryAction _lastRecoveryAction;
    private          long?           _lastObservedDownloadedBytes;
    private          DateTimeOffset? _lastUsefulActivityAtUtc;
    private          int             _recoveryCycle;

    public void Observe(DateTimeOffset now, bool isTrackedDownload, long downloadedBytes,
        long downloadRateBytesPerSecond, int openConnections)
    {
        lock (_gate)
        {
            if (!isTrackedDownload)
            {
                ResetUnsafe();
                return;
            }

            _downloadingSinceUtc ??= now;

            var sawUsefulActivity = openConnections > 0 || downloadRateBytesPerSecond > 0;
            if (_lastObservedDownloadedBytes is not null && downloadedBytes > _lastObservedDownloadedBytes.Value)
            {
                sawUsefulActivity = true;
            }

            _lastObservedDownloadedBytes = downloadedBytes;

            if (!sawUsefulActivity)
            {
                return;
            }

            _lastUsefulActivityAtUtc = now;
            _lastActionAtUtc         = null;
            _lastRecoveryAction      = DownloadRecoveryAction.None;
            _recoveryCycle           = 0;
        }
    }

    public TorrentDownloadRecoveryDecision Evaluate(DateTimeOffset now, int staleSeconds, int restartDelaySeconds)
    {
        lock (_gate)
        {
            if (_downloadingSinceUtc is null)
            {
                return TorrentDownloadRecoveryDecision.None;
            }

            var staleSinceUtc = _lastUsefulActivityAtUtc ?? _downloadingSinceUtc.Value;
            var backoffMultiplier = GetBackoffMultiplier(_recoveryCycle);
            var effectiveStaleSeconds = staleSeconds * backoffMultiplier;
            var effectiveRestartDelaySeconds = restartDelaySeconds * backoffMultiplier;
            if (now - staleSinceUtc < TimeSpan.FromSeconds(effectiveStaleSeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.None, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc, _recoveryCycle, backoffMultiplier, effectiveStaleSeconds,
                    effectiveRestartDelaySeconds
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.None)
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Refresh, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc, _recoveryCycle, backoffMultiplier, effectiveStaleSeconds,
                    effectiveRestartDelaySeconds
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.Refresh && _lastActionAtUtc is not null &&
                now - _lastActionAtUtc.Value >= TimeSpan.FromSeconds(effectiveRestartDelaySeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Restart, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc, _recoveryCycle, backoffMultiplier, effectiveStaleSeconds,
                    effectiveRestartDelaySeconds
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.Restart && _lastActionAtUtc is not null &&
                now - _lastActionAtUtc.Value >= TimeSpan.FromSeconds(effectiveStaleSeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Refresh, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc, _recoveryCycle, backoffMultiplier, effectiveStaleSeconds,
                    effectiveRestartDelaySeconds
                );
            }

            return new TorrentDownloadRecoveryDecision(
                DownloadRecoveryAction.None, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                _lastRecoveryAction, staleSinceUtc, _recoveryCycle, backoffMultiplier, effectiveStaleSeconds,
                effectiveRestartDelaySeconds
            );
        }
    }

    public void MarkRefresh(DateTimeOffset now)
    {
        lock (_gate)
        {
            _downloadingSinceUtc ??= now;
            _lastActionAtUtc     =   now;
            _lastRecoveryAction  =   DownloadRecoveryAction.Refresh;
        }
    }

    public void MarkRestart(DateTimeOffset now)
    {
        lock (_gate)
        {
            _downloadingSinceUtc ??= now;
            _lastActionAtUtc     =   now;
            _lastRecoveryAction  =   DownloadRecoveryAction.Restart;
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
        _downloadingSinceUtc        = null;
        _lastActionAtUtc            = null;
        _lastRecoveryAction         = DownloadRecoveryAction.None;
        _lastObservedDownloadedBytes = null;
        _lastUsefulActivityAtUtc    = null;
        _recoveryCycle              = 0;
    }

    private static int GetBackoffMultiplier(int recoveryCycle)
        => Math.Min(1 << Math.Min(recoveryCycle, 3), MaxBackoffMultiplier);
}

internal enum DownloadRecoveryAction
{
    None    = 0,
    Refresh = 1,
    Restart = 2,
}

internal readonly record struct TorrentDownloadRecoveryDecision(DownloadRecoveryAction Action,
    DateTimeOffset? DownloadingSinceUtc, DateTimeOffset? LastUsefulActivityAtUtc, DateTimeOffset? LastActionAtUtc,
    DownloadRecoveryAction LastRecoveryAction, DateTimeOffset StaleSinceUtc, int RecoveryCycle, int BackoffMultiplier,
    int EffectiveStaleSeconds, int EffectiveRestartDelaySeconds)
{
    public static TorrentDownloadRecoveryDecision None
        => new(
            DownloadRecoveryAction.None, null, null, null, DownloadRecoveryAction.None,
            DateTimeOffset.MinValue, 0, 1, 0, 0
        );
}
