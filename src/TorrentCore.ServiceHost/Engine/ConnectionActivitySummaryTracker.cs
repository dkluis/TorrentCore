using System.Collections.Concurrent;

namespace TorrentCore.Service.Engine;

internal sealed class ConnectionActivitySummaryTracker
{
    private readonly ConcurrentDictionary<Guid, ConnectionActivityWindow> _windows = new();

    public void RegisterPeersFound(Guid torrentId, DateTimeOffset now, int newPeers)
    {
        Update(torrentId, now, window =>
        {
            window.PeersFoundEvents++;
            window.NewPeersFound += newPeers;
        });
    }

    public void RegisterPeerConnected(Guid torrentId, DateTimeOffset now)
    {
        Update(torrentId, now, window => window.PeerConnectedEvents++);
    }

    public void RegisterPeerDisconnected(Guid torrentId, DateTimeOffset now)
    {
        Update(torrentId, now, window => window.PeerDisconnectedEvents++);
    }

    public void RegisterConnectionFailure(Guid torrentId, DateTimeOffset now, string reason)
    {
        Update(torrentId, now, window =>
        {
            window.ConnectionFailureEvents++;
            window.ConnectionFailuresByReason[reason] =
                    window.ConnectionFailuresByReason.GetValueOrDefault(reason) + 1;
        });
    }

    public IReadOnlyList<ConnectionActivitySummary> DrainReady(DateTimeOffset now, TimeSpan interval)
    {
        var summaries = new List<ConnectionActivitySummary>();
        foreach (var (torrentId, window) in _windows)
        {
            lock (window.SyncRoot)
            {
                if (now - window.WindowStartedAtUtc < interval)
                {
                    continue;
                }

                if (window.HasActivity)
                {
                    summaries.Add(
                        new ConnectionActivitySummary(
                            torrentId,
                            window.WindowStartedAtUtc,
                            now,
                            window.PeersFoundEvents,
                            window.NewPeersFound,
                            window.PeerConnectedEvents,
                            window.PeerDisconnectedEvents,
                            window.ConnectionFailureEvents,
                            window.ConnectionFailuresByReason.ToDictionary()
                        )
                    );
                }

                window.Reset(now);
            }
        }

        return summaries;
    }

    public void Remove(Guid torrentId)
    {
        _windows.TryRemove(torrentId, out _);
    }

    public void Clear()
    {
        _windows.Clear();
    }

    private void Update(Guid torrentId, DateTimeOffset now, Action<ConnectionActivityWindow> update)
    {
        var window = _windows.GetOrAdd(torrentId, _ => new ConnectionActivityWindow(now));
        lock (window.SyncRoot)
        {
            update(window);
        }
    }

    private sealed class ConnectionActivityWindow(DateTimeOffset windowStartedAtUtc)
    {
        public object SyncRoot { get; } = new();
        public DateTimeOffset WindowStartedAtUtc { get; private set; } = windowStartedAtUtc;
        public int PeersFoundEvents { get; set; }
        public int NewPeersFound { get; set; }
        public int PeerConnectedEvents { get; set; }
        public int PeerDisconnectedEvents { get; set; }
        public int ConnectionFailureEvents { get; set; }
        public Dictionary<string, int> ConnectionFailuresByReason { get; } = new(StringComparer.Ordinal);

        public bool HasActivity => PeersFoundEvents > 0 || PeerConnectedEvents > 0 ||
                                   PeerDisconnectedEvents > 0 || ConnectionFailureEvents > 0;

        public void Reset(DateTimeOffset now)
        {
            WindowStartedAtUtc = now;
            PeersFoundEvents = 0;
            NewPeersFound = 0;
            PeerConnectedEvents = 0;
            PeerDisconnectedEvents = 0;
            ConnectionFailureEvents = 0;
            ConnectionFailuresByReason.Clear();
        }
    }
}

internal sealed record ConnectionActivitySummary(
    Guid TorrentId,
    DateTimeOffset WindowStartedAtUtc,
    DateTimeOffset WindowEndedAtUtc,
    int PeersFoundEvents,
    int NewPeersFound,
    int PeerConnectedEvents,
    int PeerDisconnectedEvents,
    int ConnectionFailureEvents,
    IReadOnlyDictionary<string, int> ConnectionFailuresByReason);
