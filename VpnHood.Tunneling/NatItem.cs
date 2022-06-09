using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;

public class NatItem
{
    public ushort NatId { get; internal set; }
    public object? Tag { get; set; }
    public IPVersion IpVersion { get; }
    public ProtocolType Protocol { get; }
    public IPAddress SourceAddress { get; }
    public ushort SourcePort { get; }
    public ushort IcmpId { get; }
    public DateTime AccessTime { get; internal set; }


    public NatItem(IPPacket ipPacket)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        IpVersion = ipPacket.Version;
        Protocol = ipPacket.Protocol;
        SourceAddress = ipPacket.SourceAddress;
        AccessTime = DateTime.Now;

        switch (ipPacket.Protocol)
        {
            case ProtocolType.Tcp:
            {
                var tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                SourcePort = tcpPacket.SourcePort;
                break;
            }

            case ProtocolType.Udp:
            {
                var udpPacket = PacketUtil.ExtractUdp(ipPacket);
                SourcePort = udpPacket.SourcePort;
                break;
            }

            case ProtocolType.Icmp:
            {
                IcmpId = GetIcmpId(ipPacket);
                break;
            }

            case ProtocolType.IcmpV6:
            {
                IcmpId = GetIcmpV6Id(ipPacket);
                break;
            }

            default:
                throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
        }
    }

    //see https://datatracker.ietf.org/doc/html/rfc792
    [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>")]
    private static ushort GetIcmpId(IPPacket ipPacket)
    {
        var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
        var type = (int)icmpPacket.TypeCode >> 8;
        switch (type)
        {
            // Identifier
            case 08: // for echo message
            case 13: // for timestamp message
            case 15: // information request message
                return icmpPacket.Id != 0 ? icmpPacket.Id : icmpPacket.Sequence;

            case 00: // for echo message reply
            case 14: // for timestamp reply message.
            case 16: // for information reply message

            // (Internet Header + 64 bits of Original Data Datagram )
            case 03: // Destination Unreachable Message 
            case 04: // Source Quench Message
            case 05: // Redirect Message
            case 11: // Time Exceeded Message 
            case 12: // Parameter Problem Message
                throw new Exception($"Unexpected Icmp Code from a client! type: {type}");

            default:
                throw new Exception($"Unsupported Icmp Code! Code: {type}");
        }
    }

    //see https://datatracker.ietf.org/doc/html/rfc4443
    private static ushort GetIcmpV6Id(IPPacket ipPacket)
    {
        var icmpPacket = PacketUtil.ExtractIcmpV6(ipPacket);
        var type = (int)icmpPacket.Type;

        switch (icmpPacket.Type)
        {
            // Identifier
            case IcmpV6Type.EchoRequest: // for echo message
            case IcmpV6Type.EchoReply: // for echo message
            {
                var id = BitConverter.ToUInt16(icmpPacket.HeaderData, 2);
                var sequence = BitConverter.ToUInt16(icmpPacket.HeaderData, 4);
                return id != 0 ? id : sequence;
            }

            default:
                throw new Exception($"Unsupported Icmp Code! Code: {type}");
        }
    }

    public override bool Equals(object obj)
    {
        var src = (NatItem)obj;
        return
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