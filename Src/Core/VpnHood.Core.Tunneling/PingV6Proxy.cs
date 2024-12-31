using System.Net.NetworkInformation;
using PacketDotNet;
using PacketDotNet.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PingV6Proxy(IPacketProxyReceiver packetProxyReceiver, TimeSpan? icmpTimeout)
    : PingProxy(packetProxyReceiver, icmpTimeout)
{
    public override void Send(IPPacket ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.IcmpV6)
            throw new InvalidOperationException($"Packet is not {ProtocolType.IcmpV6}!");
        
        SendIpV6(ipPacket.Extract<IPv6Packet>());
    }
    
    private void SendIpV6(IPv6Packet ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != ProtocolType.IcmpV6)
            throw new InvalidOperationException($"Packet is not {ProtocolType.IcmpV6}!");

        // We should not use Task due its stack usage, this method is called by many session each many times!
        var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
        if (icmpPacket.Type != IcmpV6Type.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV6Type.EchoRequest}! Packet: {PacketUtil.Format(ipPacket)}");

        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, true);
        var pingData = icmpPacket.Bytes[8..];
        Ping.SendAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds, pingData, pingOptions, ipPacket);
    }

    public override Packet BuildIcmpReplyPacket(IPPacket ipPacket, PingReply pingReply)
    {
        // IcmpV6 packet generation is not fully implemented by packetNet
        // So create all packet in buffer
        var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
        icmpPacket.Type = IcmpV6Type.EchoReply;
        icmpPacket.Code = 0;
        var buffer = new byte[pingReply.Buffer.Length + 8];
        Array.Copy(icmpPacket.Bytes, 0, buffer, 0, 8);
        Array.Copy(pingReply.Buffer, 0, buffer, 8, pingReply.Buffer.Length);
        icmpPacket = new IcmpV6Packet(new ByteArraySegment(buffer));
        return icmpPacket;
    }
}