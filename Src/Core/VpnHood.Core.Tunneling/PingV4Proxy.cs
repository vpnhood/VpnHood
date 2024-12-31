using System.Net.NetworkInformation;
using PacketDotNet;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PingV4Proxy(IPacketProxyReceiver packetProxyReceiver, TimeSpan? icmpTimeout)
    : PingProxy(packetProxyReceiver, icmpTimeout)
{
    public override void Send(IPPacket ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.Icmp)
            throw new InvalidOperationException($"Packet is not {ProtocolType.Icmp}!");

        SendIpV4(ipPacket.Extract<IPv4Packet>());
    }

    private void SendIpV4(IPv4Packet ipPacket)
    {
        if (ipPacket.Protocol != ProtocolType.Icmp)
            throw new InvalidOperationException(
                $"Packet is not {ProtocolType.Icmp}! Packet: {PacketUtil.Format(ipPacket)}");

        var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
        if (icmpPacket.TypeCode != IcmpV4TypeCode.EchoRequest)
            throw new InvalidOperationException(
                $"The icmp is not {IcmpV4TypeCode.EchoRequest}! Packet: {PacketUtil.Format(ipPacket)}");

        var noFragment = (ipPacket.FragmentFlags & 0x2) != 0;
        var pingOptions = new PingOptions(ipPacket.TimeToLive - 1, noFragment);
        Ping.SendAsync(ipPacket.DestinationAddress, (int)IcmpTimeout.TotalMilliseconds, icmpPacket.Data, pingOptions, ipPacket);
    }

    public override Packet BuildIcmpReplyPacket(IPPacket ipPacket, PingReply pingReply)
    {
        var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
        icmpPacket.TypeCode = IcmpV4TypeCode.EchoReply;
        icmpPacket.Data = pingReply.Buffer;
        return icmpPacket;
    }
}