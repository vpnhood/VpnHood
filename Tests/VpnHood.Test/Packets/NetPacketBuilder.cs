using System.Net;
using System.Net.Sockets;
using PacketDotNet;

namespace VpnHood.Test.Packets;

public static class NetPacketBuilder
{
    public static byte[] RandomPacket(bool isV4)
    {
        return IPPacket.RandomPacket(isV4 ? IPVersion.IPv4 : IPVersion.IPv6).Bytes;
    }

    public static IPPacket BuildIpPacket(IPAddress sourceAddress, IPAddress destinationAddress)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new InvalidOperationException(
                $"{nameof(sourceAddress)} and {nameof(destinationAddress)}  address family must be same!");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => new IPv4Packet(sourceAddress, destinationAddress),
            AddressFamily.InterNetworkV6 => new IPv6Packet(sourceAddress, destinationAddress),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} is not supported!")
        };
    }

    public static IPPacket Parse(byte[] ipPacketBuffer)
    {
        return Packet.ParsePacket(LinkLayers.Raw, ipPacketBuffer).Extract<IPPacket>();
    }
}