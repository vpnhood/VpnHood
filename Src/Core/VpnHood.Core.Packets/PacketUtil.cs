// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Packets;

public static class PacketUtil
{
    public static int ReadPacketLength(ReadOnlySpan<byte> buffer)
    {
        var ipVersion = IpPacket.GetPacketVersion(buffer);
        return ipVersion switch {
            // v4
            IpVersion.IPv4 => IpV4Packet.GetPacketLength(buffer),
            IpVersion.IPv6 => IpV6Packet.GetPacketLength(buffer),
            _ => throw new Exception("Unknown packet version.")
        };
    }

    public static ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress, 
        byte protocol, ReadOnlySpan<byte> data)
    {
        if (sourceAddress.Length == 4 && destinationAddress.Length == 4) {
            // IPv4 pseudo header is 12 bytes
            Span<byte> pseudoHeader = stackalloc byte[12];
            sourceAddress.CopyTo(pseudoHeader[..4]);
            destinationAddress.CopyTo(pseudoHeader[4..8]);
            pseudoHeader[8] = 0;
            pseudoHeader[9] = protocol;
            pseudoHeader[10] = (byte)(data.Length >> 8);
            pseudoHeader[11] = (byte)(data.Length);
            return OnesComplementSum(pseudoHeader, data);
        }

        if (sourceAddress.Length == 16 && destinationAddress.Length == 16) {
            // IPv6 pseudo header is 40 bytes
            Span<byte> pseudoHeader = stackalloc byte[40];
            sourceAddress.CopyTo(pseudoHeader[..16]);
            destinationAddress.CopyTo(pseudoHeader[16..32]);
            pseudoHeader[32] = (byte)(data.Length >> 8);
            pseudoHeader[33] = (byte)(data.Length);
            pseudoHeader[34] = 0;
            pseudoHeader[35] = 0;
            pseudoHeader[36] = 0;
            pseudoHeader[37] = 0;
            pseudoHeader[38] = 0;
            pseudoHeader[39] = protocol; // UDP protocol number
            return OnesComplementSum(pseudoHeader, data);
        }

        throw new ArgumentException("Invalid address lengths for checksum calculation.");
    }

    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    public static ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        return ChecksumUtilsUnsafe.OnesComplementSum(pseudoHeader, data);
    }
}