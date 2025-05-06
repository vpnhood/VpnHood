using System.Net;
using PacketDotNet;

// ReSharper disable UnusedMember.Global
namespace VpnHood.Core.Packets;

public static class PacketUtil
{
    public static ushort ReadPacketLength(byte[] buffer, int bufferIndex)
    {
        var version = buffer[bufferIndex] >> 4;

        // v4
        if (version == 4) {
            var packetLength = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 2));
            if (packetLength < 20)
                throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");
            return packetLength;
        }

        // v6
        if (version == 6) {
            var payload = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, bufferIndex + 4));
            return (ushort)(40 + payload); //header + payload
        }

        // unknown
        throw new Exception("Unknown packet version!");
    }

    public static IPPacket ReadNextPacket(byte[] buffer, ref int bufferIndex)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));

        var packetLength = ReadPacketLength(buffer, bufferIndex);
        var packet = Packet.ParsePacket(LinkLayers.Raw, buffer[bufferIndex..(bufferIndex + packetLength)])
            .Extract<IPPacket>();
        bufferIndex += packetLength;
        return packet;
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
            return ComputeChecksum(pseudoHeader, data);
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
            return ComputeChecksum(pseudoHeader, data);
        }

        throw new ArgumentException("Invalid address lengths for checksum calculation.");
    }

    public static ushort ComputeChecksum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        uint sum = 0;

        sum += SumWords(pseudoHeader);
        sum += SumWords(data);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }

    private static uint SumWords(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i < data.Length; i += 2) {
            var word = (ushort)(data[i] << 8 | (i + 1 < data.Length ? data[i + 1] : 0));
            sum += word;
        }
        return sum;
    }

}
