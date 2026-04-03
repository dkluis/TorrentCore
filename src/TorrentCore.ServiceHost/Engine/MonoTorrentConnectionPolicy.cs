using System.Net;
using System.Net.Sockets;
using MonoTorrent.Connections;

namespace TorrentCore.Service.Engine;

internal static class MonoTorrentConnectionPolicy
{
    public static List<EncryptionType> CreateAllowedEncryption()
    {
        return
        [
            EncryptionType.PlainText,
            EncryptionType.RC4Header,
            EncryptionType.RC4Full,
        ];
    }

    public static Dictionary<string, IPEndPoint> CreateListenEndPoints(int port)
    {
        var endPoints = new Dictionary<string, IPEndPoint>
        {
            ["ipv4"] = new(IPAddress.Any, port),
        };

        if (Socket.OSSupportsIPv6)
        {
            endPoints["ipv6"] = new(IPAddress.IPv6Any, port);
        }

        return endPoints;
    }
}
