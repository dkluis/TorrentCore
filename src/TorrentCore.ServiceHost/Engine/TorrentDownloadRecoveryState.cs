namespace TorrentCore.Service.Engine;

internal sealed class TorrentDownloadRecoveryState
{
    private readonly object          _gate = new();
    private          DateTimeOffset? _downloadingSinceUtc;
    private          DateTimeOffset? _lastActionAtUtc;
    private          DownloadRecoveryAction _lastRecoveryAction;
    private          long?           _lastObservedDownloadedBytes;
    private          DateTimeOffset? _lastUsefulActivityAtUtc;

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
            if (now - staleSinceUtc < TimeSpan.FromSeconds(staleSeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.None, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.None)
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Refresh, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.Refresh && _lastActionAtUtc is not null &&
                now - _lastActionAtUtc.Value >= TimeSpan.FromSeconds(restartDelaySeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Restart, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc
                );
            }

            if (_lastRecoveryAction == DownloadRecoveryAction.Restart && _lastActionAtUtc is not null &&
                now - _lastActionAtUtc.Value >= TimeSpan.FromSeconds(staleSeconds))
            {
                return new TorrentDownloadRecoveryDecision(
                    DownloadRecoveryAction.Refresh, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                    _lastRecoveryAction, staleSinceUtc
                );
            }

            return new TorrentDownloadRecoveryDecision(
                DownloadRecoveryAction.None, _downloadingSinceUtc, _lastUsefulActivityAtUtc, _lastActionAtUtc,
                _lastRecoveryAction, staleSinceUtc
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
    }
}

internal enum DownloadRecoveryAction
{
    None    = 0,
    Refresh = 1,
    Restart = 2,
}

internal readonly record struct TorrentDownloadRecoveryDecision(DownloadRecoveryAction Action,
    DateTimeOffset? DownloadingSinceUtc, DateTimeOffset? LastUsefulActivityAtUtc, DateTimeOffset? LastActionAtUtc,
    DownloadRecoveryAction LastRecoveryAction, DateTimeOffset StaleSinceUtc)
{
    public static TorrentDownloadRecoveryDecision None
        => new(
            DownloadRecoveryAction.None, null, null, null, DownloadRecoveryAction.None,
            DateTimeOffset.MinValue
        );
}
