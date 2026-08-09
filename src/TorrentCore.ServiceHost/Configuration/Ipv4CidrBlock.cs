using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace TorrentCore.Service.Configuration;

internal readonly record struct Ipv4CidrBlock(uint Network, int PrefixLength)
{
    public static bool TryParse(string? value, out Ipv4CidrBlock block)
    {
        block = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefixLength) ||
            prefixLength is < 0 or > 32)
        {
            return false;
        }

        var addressValue = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
        block = new Ipv4CidrBlock(addressValue & mask, prefixLength);
        return true;
    }

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var addressValue = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        var mask = PrefixLength == 0 ? 0U : uint.MaxValue << (32 - PrefixLength);
        return (addressValue & mask) == Network;
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, Network);
        return $"{new IPAddress(bytes)}/{PrefixLength}";
    }
}
