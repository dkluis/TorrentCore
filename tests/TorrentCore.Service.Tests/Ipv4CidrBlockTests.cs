using System.Net;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class Ipv4CidrBlockTests
{
    [Fact]
    public void TryParse_NormalizesHostBitsAndMatchesOnlyTheNetwork()
    {
        Assert.True(Ipv4CidrBlock.TryParse("198.51.100.42/24", out var block));

        Assert.Equal("198.51.100.0/24", block.ToString());
        Assert.True(block.Contains(IPAddress.Parse("198.51.100.1")));
        Assert.True(block.Contains(IPAddress.Parse("198.51.100.255")));
        Assert.False(block.Contains(IPAddress.Parse("198.51.101.1")));
        Assert.False(block.Contains(IPAddress.Parse("2001:db8::1")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("47.0.0.0")]
    [InlineData("47.0.0.0/-1")]
    [InlineData("47.0.0.0/33")]
    [InlineData("2001:db8::/32")]
    public void TryParse_RejectsInvalidOrNonIpv4Values(string? value)
    {
        Assert.False(Ipv4CidrBlock.TryParse(value, out _));
    }
}
