using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

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

    public static bool IsPacketMessage(IpPacket ipPacket)
    {
        return ipPacket.Protocol == IpProtocol.Udp &&
               IPAddress.None.SpanEquals(ipPacket.DestinationAddressSpan);
    }

    public static IPacketMessage? ReadMessage(IpPacket ipPacket)
    {
        if (!IsPacketMessage(ipPacket))
            return null;

        var udpPacket = ipPacket.ExtractUdp();
        if (udpPacket.Payload.Length < 2)
            throw new InvalidDataException("The datagram message is too short to read version and message code.");

        // check version
        var version = udpPacket.Payload.Span[0];
        if (version != 1)
            throw new NotSupportedException($"The datagram message version is not supported. Version: {version}");

        // check message code
        using var stream = new MemoryStream(udpPacket.Payload[2..].ToArray());
        var messageCode = (PacketMessageCode)udpPacket.Payload.Span[1];
        return messageCode switch {
            PacketMessageCode.ClosePacketChannel => StreamUtils.ReadObject<ClosePacketMessage>(stream),
            _ => throw new NotSupportedException($"Unknown Datagram Message messageCode. MessageCode: {messageCode}")
        };
    }
}