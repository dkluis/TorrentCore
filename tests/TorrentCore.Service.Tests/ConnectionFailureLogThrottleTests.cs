using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class ConnectionFailureLogThrottleTests
{
    [Fact]
    public void RegisterAttempt_ThrottlesAfterBurstLimitWithinWindow()
    {
        var throttle = new ConnectionFailureLogThrottle(burstLimit: 2, windowSeconds: 60);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now));
        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now.AddSeconds(1)));
        Assert.Equal(ConnectionFailureLogDecision.ThrottleNotice, throttle.RegisterAttempt("torrent-a", now.AddSeconds(2)));
        Assert.Equal(ConnectionFailureLogDecision.Suppress, throttle.RegisterAttempt("torrent-a", now.AddSeconds(3)));
    }

    [Fact]
    public void RegisterAttempt_ResetsAfterWindowExpires()
    {
        var throttle = new ConnectionFailureLogThrottle(burstLimit: 1, windowSeconds: 10);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now));
        Assert.Equal(ConnectionFailureLogDecision.ThrottleNotice, throttle.RegisterAttempt("torrent-a", now.AddSeconds(1)));
        Assert.Equal(ConnectionFailureLogDecision.Log, throttle.RegisterAttempt("torrent-a", now.AddSeconds(11)));
    }
}
