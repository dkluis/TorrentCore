using System.Net;
using System.Net.Sockets;
using MonoTorrent.Connections;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

internal static class MonoTorrentConnectionPolicy
{
    public static List<EncryptionType> CreateAllowedEncryption(TorrentEncryptionMode mode)
    {
        return mode switch
        {
            TorrentEncryptionMode.PlainTextPreferred =>
                [EncryptionType.PlainText, EncryptionType.RC4Header, EncryptionType.RC4Full],
            TorrentEncryptionMode.EncryptedPreferred =>
                [EncryptionType.RC4Header, EncryptionType.RC4Full, EncryptionType.PlainText],
            TorrentEncryptionMode.EncryptedRequired =>
                [EncryptionType.RC4Header, EncryptionType.RC4Full],
            _ => [EncryptionType.RC4Header, EncryptionType.RC4Full, EncryptionType.PlainText],
        };
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
