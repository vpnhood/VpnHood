using System;
using System.Net;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;

public class NatItemEx : NatItem
{
    public NatItemEx(IPPacket ipPacket) : base(ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        DestinationAddress = ipPacket.DestinationAddress;

        switch (ipPacket.Protocol)
        {
            case ProtocolType.Tcp:
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                DestinationPort = tcpPacket.DestinationPort;
                break;
            }

            case ProtocolType.Udp:
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                DestinationPort = udpPacket.DestinationPort;
                break;
            }

            default:
                throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
        }
    }

    public IPAddress DestinationAddress { get; }
    public ushort DestinationPort { get; }

    public override bool Equals(object obj)
    {
        var src = (NatItemEx) obj;
        return
            base.Equals(obj) &&
            Equals(DestinationAddress, src.DestinationAddress) &&
            Equals(DestinationPort, src.DestinationPort);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), DestinationAddress, DestinationPort);
    }

    public override string ToString()
    {
        return
            $"{Protocol}:{NatId}, LocalEp: {VhLogger.Format(SourceAddress)}:{SourcePort}, RemoteEp: {VhLogger.Format(DestinationAddress)}:{DestinationPort}";
    }
}