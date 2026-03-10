using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class ConnectionFailureLogThrottleTests
{
    [Fact]
    public void RegisterAttempt_ThrottlesAfterBurstLimitWithinWindow()
    {
        var throttle = new ConnectionFailureLogThrottle();
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now, burstLimit: 2, windowSeconds: 60));
        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now.AddSeconds(1), burstLimit: 2, windowSeconds: 60));
        Assert.Equal(ConnectionFailureLogDecision.ThrottleNotice, throttle.RegisterAttempt("torrent-a", now.AddSeconds(2), burstLimit: 2, windowSeconds: 60));
        Assert.Equal(ConnectionFailureLogDecision.Suppress, throttle.RegisterAttempt("torrent-a", now.AddSeconds(3), burstLimit: 2, windowSeconds: 60));
    }

    [Fact]
    public void RegisterAttempt_ResetsAfterWindowExpires()
    {
        var throttle = new ConnectionFailureLogThrottle();
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now, burstLimit: 1, windowSeconds: 10));
        Assert.Equal(ConnectionFailureLogDecision.ThrottleNotice, throttle.RegisterAttempt("torrent-a", now.AddSeconds(1), burstLimit: 1, windowSeconds: 10));
        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now.AddSeconds(11), burstLimit: 1, windowSeconds: 10));
    }
}
