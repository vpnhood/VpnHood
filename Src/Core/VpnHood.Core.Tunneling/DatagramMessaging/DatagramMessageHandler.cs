using System.Net;
using PacketDotNet;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.DatagramMessaging;

public static class DatagramMessageHandler
{
    private static DatagramMessageCode GetMessageCode(DatagramBaseMessage requestMessage)
    {
        if (requestMessage is CloseDatagramMessage) return DatagramMessageCode.CloseDatagramChannel;
        throw new ArgumentException("Could not detect version code for this datagram message.");
    }

    public static IPPacket CreateMessage(DatagramBaseMessage requestMessage)
    {
        // building request
        using var mem = new MemoryStream();
        mem.WriteByte(1);
        mem.WriteByte((byte)GetMessageCode(requestMessage));
        StreamUtils.WriteObject(mem, requestMessage);
        var ipPacket = PacketUtil.CreateUdpPacket(new IPEndPoint(0, 0), new IPEndPoint(0, 0), mem.ToArray(), false);
        return ipPacket;
    }

    public static bool IsDatagramMessage(IPPacket ipPacket)
    {
        return ipPacket.DestinationAddress.Equals(new IPAddress(0)) && ipPacket.Protocol == ProtocolType.Udp;
    }

    public static DatagramBaseMessage ReadMessage(IPPacket ipPacket)
    {
        if (!IsDatagramMessage(ipPacket))
            throw new ArgumentException("packet is not a Datagram message.", nameof(ipPacket));

        var udpPacket = PacketUtil.ExtractUdp(ipPacket);

        // read version and messageCode
        var buffer = new byte[2];
        var stream = new MemoryStream(udpPacket.PayloadData);
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