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
        if (request is ClosePacketMessage) return PacketMessageCode.CloseDatagramChannel;
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

        //todo: use buffer directly instead of MemoryStream for performance
        // read version and messageCode
        var buffer = new byte[2];
        var stream = new MemoryStream(udpPacket.Payload.ToArray());
        var res = stream.Read(buffer, 0, buffer.Length);
        if (res != buffer.Length)
            throw new Exception($"Invalid datagram message length. Length: {buffer.Length}");

        // check version
        var version = buffer[0];
        if (version != 1)
            throw new NotSupportedException($"The datagram message version is not supported. Version: {version}");

        // check message code
        var messageCode = (PacketMessageCode)buffer[1];
        return messageCode switch {
            PacketMessageCode.CloseDatagramChannel => StreamUtils.ReadObject<ClosePacketMessage>(stream),
            _ => throw new NotSupportedException($"Unknown Datagram Message messageCode. MessageCode: {messageCode}")
        };
    }
}