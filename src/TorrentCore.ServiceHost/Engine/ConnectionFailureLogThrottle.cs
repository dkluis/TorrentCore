#region

using System.Collections.Concurrent;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class ConnectionFailureLogThrottle
{
    private readonly ConcurrentDictionary<string, ConnectionFailureLogWindow> _windows = new();

    public ConnectionFailureLogDecision RegisterAttempt(string key, DateTimeOffset now, int burstLimit,
        int                                                    windowSeconds)
    {
        var window = _windows.GetOrAdd(key, _ => new ConnectionFailureLogWindow(now));

        lock (window.SyncRoot)
        {
            if ((now - window.WindowStartedAtUtc).TotalSeconds >= windowSeconds)
            {
                window.WindowStartedAtUtc   = now;
                window.LoggedCount          = 0;
                window.ThrottleNoticeLogged = false;
            }

            if (window.LoggedCount < burstLimit)
            {
                window.LoggedCount++;
                return ConnectionFailureLogDecision.Log;
            }

            if (!window.ThrottleNoticeLogged)
            {
                window.ThrottleNoticeLogged = true;
                return ConnectionFailureLogDecision.ThrottleNotice;
            }

            return ConnectionFailureLogDecision.Suppress;
        }
    }

    public void Clear() { _windows.Clear(); }
    private sealed class ConnectionFailureLogWindow(DateTimeOffset windowStartedAtUtc)
    {
        public object         SyncRoot             { get; }      = new();
        public DateTimeOffset WindowStartedAtUtc   { get; set; } = windowStartedAtUtc;
        public int            LoggedCount          { get; set; }
        public bool           ThrottleNoticeLogged { get; set; }
    }
}
public enum ConnectionFailureLogDecision
{
    Log            = 0,
    ThrottleNotice = 1,
    Suppress       = 2,
}
