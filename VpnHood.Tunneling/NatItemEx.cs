using PacketDotNet;
using System;
using System.Net;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class NatItemEx : NatItem
    {
        public IPAddress DestinationAddress { get; }
        public ushort DestinationPort { get; }

        public NatItemEx(IPPacket ipPacket) : base(ipPacket)
        {
            DestinationAddress = ipPacket.DestinationAddress;

            switch (ipPacket.Protocol)
            {
                case ProtocolType.Tcp:
                    {
                        var tcpPacket = ipPacket.Extract<TcpPacket>();
                        DestinationPort = tcpPacket.DestinationPort;
                        break;
                    }

                case ProtocolType.Udp:
                    {
                        var udpPacket = ipPacket.Extract<UdpPacket>();
                        DestinationPort = udpPacket.DestinationPort;
                        break;
                    }

                default:
                    throw new NotSupportedException($"{ipPacket.Protocol} is not yet supported by this NAT!");
            }
        }

        public override bool Equals(object obj)
        {
            var src = (NatItemEx)obj;
            return
                base.Equals(obj) &&
                Equals(DestinationAddress, src.DestinationAddress) &&
                Equals(DestinationPort, src.DestinationPort);
        }

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), DestinationAddress, DestinationPort);
        public override string ToString() => $"{Protocol}:{NatId}, LocalEp: {Logger.Format(SourceAddress)}:{SourcePort}, RemoteEp: {Logger.Format(DestinationAddress)}:{DestinationPort}";
    }
}
