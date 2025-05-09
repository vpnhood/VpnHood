using System.Net;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.DatagramMessaging;

public static class DatagramMessageHandler
{
    private static DatagramMessageCode GetMessageCode(DatagramBaseMessage requestMessage)
    {
        if (requestMessage is CloseDatagramMessage) return DatagramMessageCode.CloseDatagramChannel;
        throw new ArgumentException("Could not detect version code for this datagram message.");
    }

    private static readonly IPEndPoint NoneEndPoint = new(IPAddress.None, 0);
    public static IpPacket CreateMessage(DatagramBaseMessage requestMessage)
    {
        // building request
        using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte((byte)GetMessageCode(requestMessage));
        StreamUtils.WriteObject(mem, requestMessage);
        var ipPacket = PacketBuilder.BuildUdp(NoneEndPoint, NoneEndPoint, mem.ToArray());
        return ipPacket;
    }

    public static bool IsDatagramMessage(IpPacket ipPacket)
    {
        return ipPacket.Protocol == IpProtocol.Udp &&
               ipPacket.DestinationAddressSpan.SequenceEqual(IPAddress.None.GetAddressBytes());
    }

    public static DatagramBaseMessage ReadMessage(IpPacket ipPacket)
    {
        if (!IsDatagramMessage(ipPacket))
            throw new ArgumentException("packet is not a Datagram message.", nameof(ipPacket));

        var udpPacket = ipPacket.ExtractUdp();

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
        var messageCode = (DatagramMessageCode)buffer[1];
        return messageCode switch {
            DatagramMessageCode.CloseDatagramChannel => StreamUtils.ReadObject<CloseDatagramMessage>(stream),
            _ => throw new NotSupportedException($"Unknown Datagram Message messageCode. MessageCode: {messageCode}")
        };
    }
}