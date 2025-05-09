using System.Net;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Tunneling;

public class NatItemEx : NatItem
{
    public NatItemEx(IpPacket ipPacket) : base(ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        DestinationAddress = ipPacket.DestinationAddress;

        switch (ipPacket.Protocol) {
            case IpProtocol.Tcp: {
                var tcpPacket = ipPacket.ExtractTcp();
                DestinationPort = tcpPacket.DestinationPort;
                break;
            }

            case IpProtocol.Udp: {
                var udpPacket = ipPacket.ExtractUdp();
                DestinationPort = udpPacket.DestinationPort;
                break;
            }

            default:
                throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
        }
    }

    public IPAddress DestinationAddress { get; }
    public ushort DestinationPort { get; }

    public override bool Equals(object? obj)
    {
        return
            obj is NatItemEx src &&
            base.Equals(src) &&
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
            $"{Protocol}:{NatId}, " +
            $"LocalEp: {VhLogger.Format(SourceAddress)}:{SourcePort}, " +
            $"RemoteEp: {VhLogger.Format(DestinationAddress)}:{DestinationPort}";
    }
}