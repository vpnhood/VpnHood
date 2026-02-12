using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Server;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling.NetFiltering;

namespace VpnHood.Test.Providers;

public class TestNetFilterClient : NetFilter
{
    private readonly TestNetFilterIps _filterIps;

    protected override bool IsIpAddressBlocked(IPAddress ipAddress)
    {
        return
            _filterIps.BlockedIpAddresses.Contains(ipAddress) || 
            base.IsIpAddressBlocked(ipAddress);
    }

    public TestNetFilterClient(TestNetFilterIps filterIps)
    {
        _filterIps = filterIps;
        BlockLoopback = false;
    }

    public override IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint)
    {
        // map IPv4
        for (var i = 0; i < _filterIps.RemoteTestIpV4s.Count; i++) {
            if (requestEndPoint.Address.Equals(_filterIps.RemoteTestIpV4s[i])) 
                return new IPEndPoint(_filterIps.LocalTestIpV4s[i], requestEndPoint.Port);
        }

        // map IPv6
        if (requestEndPoint.Address.Equals(_filterIps.RemoteTestIpV6)) {
            return new IPEndPoint(IPAddress.IPv6Loopback, requestEndPoint.Port);
        }

        return base.ProcessRequest(protocol, requestEndPoint);
    }

    public override IpPacket? ProcessRequest(IpPacket ipPacket)
    {
        var result = base.ProcessRequest(ipPacket);
        if (result == null) return null;
        ipPacket = result;

        switch (ipPacket.Protocol) {
            case IpProtocol.Udp: {
                var udpPacket = ipPacket.ExtractUdp();
                var newEndPoint = ProcessRequest(ipPacket.Protocol,
                    new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort));
                if (newEndPoint == null) return null;
                ipPacket.DestinationAddress = newEndPoint.Address;
                udpPacket.DestinationPort = (ushort)newEndPoint.Port;
                ipPacket.UpdateAllChecksums();
                return ipPacket;
            }
            case IpProtocol.Tcp: {
                var tcpPacket = ipPacket.ExtractTcp();
                var newEndPoint = ProcessRequest(ipPacket.Protocol,
                    new IPEndPoint(ipPacket.DestinationAddress, tcpPacket.DestinationPort));
                if (newEndPoint == null) return null;
                ipPacket.DestinationAddress = newEndPoint.Address;
                tcpPacket.DestinationPort = (ushort)newEndPoint.Port;
                ipPacket.UpdateAllChecksums();
                return ipPacket;
            }
            case IpProtocol.IcmpV4 or IpProtocol.IcmpV6: {
                var newEndPoint = ProcessRequest(ipPacket.Protocol, new IPEndPoint(ipPacket.DestinationAddress, 0));
                if (newEndPoint == null) return null;
                ipPacket.DestinationAddress = newEndPoint.Address;
                ipPacket.UpdateAllChecksums();
                return ipPacket;
            }
            default:
                return ipPacket;
        }
    }

    public override IpPacket ProcessReply(IpPacket ipPacket)
    {
        switch (ipPacket.Protocol) {
            case IpProtocol.Udp: {
                var udpPacket = ipPacket.ExtractUdp();
                if (NetMapR.TryGetValue(
                        Tuple.Create(ipPacket.Protocol, new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort)),
                        out var newEndPoint1)) {
                    ipPacket.SourceAddress = newEndPoint1.Address;
                    udpPacket.SourcePort = (ushort)newEndPoint1.Port;
                    ipPacket.UpdateAllChecksums();
                }

                break;
            }

            case IpProtocol.Tcp: {
                var tcpPacket = ipPacket.ExtractTcp();
                if (NetMapR.TryGetValue(
                        Tuple.Create(ipPacket.Protocol, new IPEndPoint(ipPacket.SourceAddress, tcpPacket.SourcePort)),
                        out var tcpEndPoint)) {
                    ipPacket.SourceAddress = tcpEndPoint.Address;
                    tcpPacket.SourcePort = (ushort)tcpEndPoint.Port;
                    ipPacket.UpdateAllChecksums();
                }

                break;
            }

            case IpProtocol.IcmpV4 or IpProtocol.IcmpV6:
                if (NetMapR.TryGetValue(
                        Tuple.Create(ipPacket.Protocol, new IPEndPoint(ipPacket.SourceAddress, 0)),
                        out var icmpEndPoint)) {
                    ipPacket.SourceAddress = icmpEndPoint.Address;
                    ipPacket.UpdateAllChecksums();
                }

                break;
        }

        return ipPacket;
    }
}