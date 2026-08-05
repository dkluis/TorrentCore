using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class LaunchAgentServiceRestartSchedulerTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("com.torrentcore.service")]
    public void ResolveServiceLaunchAgentLabelUsesSupportedServiceLabel(string launchdContextName)
    {
        var result = LaunchAgentServiceRestartScheduler.ResolveServiceLaunchAgentLabel(launchdContextName);

        Assert.Equal(LaunchAgentServiceRestartScheduler.ServiceLaunchAgentLabel, result);
        Assert.Equal("com.torrentcore.service", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveServiceLaunchAgentLabelRequiresLaunchdContext(string? launchdContextName)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => LaunchAgentServiceRestartScheduler.ResolveServiceLaunchAgentLabel(launchdContextName)
        );

        Assert.Contains("running under launchd", exception.Message, StringComparison.Ordinal);
    }
}
