using System.Diagnostics.CodeAnalysis;
using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class NatItem
{
    public ushort NatId { get; internal set; }
    public object? CustomData { get; set; }
    public IpVersion IpVersion { get; }
    public IpProtocol Protocol { get; }
    public IPAddress SourceAddress { get; }
    public ushort SourcePort { get; }
    public ushort IcmpId { get; }
    public DateTime AccessTime { get; internal set; }

    public NatItem(IpPacket ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        IpVersion = ipPacket.Version;
        Protocol = ipPacket.Protocol;
        SourceAddress = ipPacket.SourceAddress;
        AccessTime = FastDateTime.Now;

        switch (ipPacket.Protocol) {
            case IpProtocol.Tcp: {
                    var tcpPacket = ipPacket.ExtractTcp();
                    SourcePort = tcpPacket.SourcePort;
                    break;
                }

            case IpProtocol.Udp: {
                    var udpPacket = ipPacket.ExtractUdp();
                    SourcePort = udpPacket.SourcePort;
                    break;
                }

            case IpProtocol.IcmpV4: {
                    IcmpId = GetIcmpV4Id(ipPacket);
                    break;
                }

            case IpProtocol.IcmpV6: {
                    IcmpId = GetIcmpV6Id(ipPacket);
                    break;
                }

            default:
                throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
        }
    }

    //see https://datatracker.ietf.org/doc/html/rfc792
    [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>")]
    private static ushort GetIcmpV4Id(IpPacket ipPacket)
    {
        var icmpPacket = ipPacket.ExtractIcmpV4();
        if (icmpPacket.Type is IcmpV4Type.EchoRequest or IcmpV4Type.EchoReply)
            return icmpPacket.Identifier;

        throw new Exception($"Unsupported IcmpV4 Type for NAT. Type: {icmpPacket.Type}");
    }

    //see https://datatracker.ietf.org/doc/html/rfc4443
    private static ushort GetIcmpV6Id(IpPacket ipPacket)
    {
        var icmpPacket = ipPacket.ExtractIcmpV6();
        if (icmpPacket.Type is IcmpV6Type.EchoRequest or IcmpV6Type.EchoReply)
            return icmpPacket.Identifier;

        throw new Exception($"Unsupported IcmpV6 Type for NAT. Type: {icmpPacket.Type}");
    }

    public override bool Equals(object? obj)
    {
        return
            obj is NatItem src &&
            Equals(IpVersion, src.IpVersion) &&
            Equals(Protocol, src.Protocol) &&
            Equals(SourceAddress, src.SourceAddress) &&
            Equals(SourcePort, src.SourcePort) &&
            Equals(IcmpId, src.IcmpId);
    }

    // AccessTime is not counted
    public override int GetHashCode()
    {
        return HashCode.Combine(IpVersion, Protocol, SourceAddress, SourcePort, IcmpId);
    }

    public override string ToString()
    {
        return $"{Protocol}:{NatId}, LocalEp: {VhLogger.Format(SourceAddress)}:{SourcePort}";
    }
}