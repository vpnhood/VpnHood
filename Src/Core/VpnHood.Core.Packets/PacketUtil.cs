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
}