using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Core.Tunneling.DatagramMessaging;

public static class PacketMessageHandler
{
    private static readonly IPEndPoint NoneEndPoint = new(IPAddress.None, 0);

    private static PacketMessageCode GetMessageCode(IPacketMessage request)
    {
        if (request is ClosePacketMessage) return PacketMessageCode.ClosePacketChannel;
        throw new ArgumentException("Could not detect version code for this datagram message.");
    }

    public static IpPacket CreateMessage(IPacketMessage request)
    {
        // building request
        using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte((byte)GetMessageCode(request));
        StreamUtils.WriteObject(mem, request);
        var ipPacket = PacketBuilder.BuildUdp(NoneEndPoint, NoneEndPoint, mem.ToArray());
        return ipPacket;
    }

    // a packet message with no message, which a UDP receiver drops unread: for a datagram that only has to arrive
    public static IpPacket CreateEmptyMessage()
    {
        return PacketBuilder.BuildUdp(NoneEndPoint, NoneEndPoint, []);
    }

    public static bool IsPacketMessage(IpPacket ipPacket)
    {
        return ipPacket.Protocol == IpProtocol.Udp && (
            // Compatibility (2025-07): a client before 7.2.728 sends its packet messages to 255.255.255.255; drop
            // once MinClientProtocolVersion is 10, as such a client speaks 9 at most
            IPAddress.None.SpanEquals(ipPacket.DestinationAddressSpan) ||
            IPAddress.Any.SpanEquals(ipPacket.DestinationAddressSpan));
    }

    public static IPacketMessage? ReadMessage(IpPacket ipPacket)
    {
        if (!IsPacketMessage(ipPacket))
            return null;

        var udpPacket = ipPacket.ExtractUdp();
        if (udpPacket.Payload.Length < 2)
            throw new InvalidDataException("The packet message is too short to read version and message code.");

        // check version
        var version = udpPacket.Payload.Span[0];
        if (version != 1)
            throw new NotSupportedException(
                $"The packet message version is not supported. Version: {version}. Packet: {ipPacket}");

        // check message code
        var messageCode = (PacketMessageCode)udpPacket.Payload.Span[1];

        using var stream = new MemoryStream(udpPacket.Payload[2..].ToArray());
        return messageCode switch {
            PacketMessageCode.ClosePacketChannel => StreamUtils.ReadObject<ClosePacketMessage>(stream),
            _ => throw new NotSupportedException($"Unknown Datagram Message messageCode. MessageCode: {messageCode}")
        };
    }
}