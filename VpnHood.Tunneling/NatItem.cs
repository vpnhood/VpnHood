using PacketDotNet;
using System;
using System.Net;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class NatItem
    {
        public ushort NatId { get; internal set; }
        public object? Tag { get; set; }
        public ProtocolType Protocol { get; }
        public IPAddress SourceAddress { get; }
        public ushort SourcePort { get; }
        public ushort IcmpId { get; }
        public DateTime AccessTime { get; internal set; }

        public NatItem(IPPacket ipPacket)
        {
            if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

            SourceAddress = ipPacket.SourceAddress;
            Protocol = ipPacket.Protocol;
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

                default:
                    throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
            }
        }


        //see https://tools.ietf.org/html/rfc792
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>")]
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
                    return icmpPacket.Id;

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

        public override bool Equals(object obj)
        {
            var src = (NatItem)obj;
            return
                Equals(Protocol, src.Protocol) &&
                Equals(SourceAddress, src.SourceAddress) &&
                Equals(SourcePort, src.SourcePort) &&
                Equals(IcmpId, src.IcmpId);
        }

        // AccessTime is not counted
        public override int GetHashCode() => HashCode.Combine(Protocol, SourceAddress, SourcePort, IcmpId);

        public override string ToString() => $"{Protocol}:{NatId}, LocalEp: {VhLogger.Format(SourceAddress)}:{SourcePort}";

    }
}
