using System.Net;
using System.Net.Sockets;
using MonoTorrent.Connections;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentConnectionPolicyTests
{
    [Fact]
    public void CreateAllowedEncryption_PrefersPlainTextBeforeRc4Fallback()
    {
        var allowedEncryption = MonoTorrentConnectionPolicy.CreateAllowedEncryption();

        Assert.Equal(
            [EncryptionType.PlainText, EncryptionType.RC4Header, EncryptionType.RC4Full], allowedEncryption
        );
    }

    [Fact]
    public void CreateListenEndPoints_AlwaysIncludesIpv4_AndAddsIpv6WhenSupported()
    {
        var endPoints = MonoTorrentConnectionPolicy.CreateListenEndPoints(55_123);

        Assert.Equal(new IPEndPoint(IPAddress.Any, 55_123), endPoints["ipv4"]);
        Assert.Equal(Socket.OSSupportsIPv6, endPoints.ContainsKey("ipv6"));

        if (Socket.OSSupportsIPv6)
        {
            Assert.Equal(new IPEndPoint(IPAddress.IPv6Any, 55_123), endPoints["ipv6"]);
        }
    }
}
