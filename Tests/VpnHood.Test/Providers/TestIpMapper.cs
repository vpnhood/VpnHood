using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net.Extensions;
using VpnHood.Core.Server;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Providers;

public class TestIpMapper : IIpMapper
{
    private readonly TestNetFilterIps _filterIps;

    protected override bool IsIpAddressBlocked(IPAddress ipAddress)
    {
        return
            _filterIps.BlockedIpAddresses.Contains(ipAddress) ||
            base.IsIpAddressBlocked(ipAddress);
    }

    public TestIpMapper(TestNetFilterIps filterIps)
    {
        _filterIps = filterIps;
        BlockLoopback = false;
    }

    public override IpPacket? ProcessRequest(IpPacket ipPacket)
    {
        var result = base.ProcessRequest(ipPacket);
        if (result == null) return null;
        ipPacket = result;

        var oldEndPoint = ipPacket.GetDestinationEndPoint().ToIPEndPoint();
        var newEndPoint = ProcessRequest(ipPacket.Protocol, oldEndPoint);
        if (newEndPoint == null) return null;
        if (!newEndPoint.Equals(oldEndPoint)) {
            ipPacket.SetDestinationEndPoint(new IpEndPointValue(newEndPoint.Address, newEndPoint.Port));
            ipPacket.UpdateAllChecksums();
        }

        return ipPacket;
    }

    public override IpPacket? ProcessReply(IpPacket ipPacket)
    {
        var oldEndPoint = ipPacket.GetSourceEndPoint();
        var newٍEndPoint = ProcessReply(ipPacket.GetSourceEndPoint());
        if (newٍEndPoint == null) return null;
        if (newٍEndPoint != oldEndPoint) {
            ipPacket.SetSourceEndPoint(newٍEndPoint.Value);
            ipPacket.UpdateAllChecksums();
        }

        return ipPacket;
    }

    public override IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint)
    {
        // map IPv6
        if (requestEndPoint.Address.Equals(_filterIps.RemoteTestIpV4)) {
            return new IPEndPoint(_filterIps.LocalTestIpV4, requestEndPoint.Port);
        }

        // map IPv6
        if (requestEndPoint.Address.Equals(_filterIps.RemoteTestIpV6)) {
            return new IPEndPoint(_filterIps.LocalTestIpV6, requestEndPoint.Port);
        }

        // map IPv4s
        for (var i = 0; i < _filterIps.RemoteTestIpV4s.Count; i++) {
            if (requestEndPoint.Address.Equals(_filterIps.RemoteTestIpV4s[i]))
                return new IPEndPoint(_filterIps.LocalTestIpV4s[i], requestEndPoint.Port);
        }

        return base.ProcessRequest(protocol, requestEndPoint);
    }

    private IpEndPointValue? ProcessReply(IpEndPointValue replyEndPoint)
    {
        // map IPv6
        if (replyEndPoint.Address.Equals(_filterIps.LocalTestIpV6)) {
            return replyEndPoint with { Address = _filterIps.RemoteTestIpV6 };
        }

        // map IPv4 
        if (replyEndPoint.Address.Equals(_filterIps.LocalTestIpV4)) {
            return replyEndPoint with { Address = _filterIps.RemoteTestIpV4 };
        }

        // map IPv4s
        for (var i = 0; i < _filterIps.LocalTestIpV4s.Count; i++) {
            if (replyEndPoint.Address.Equals(_filterIps.LocalTestIpV4s[i]))
                return replyEndPoint with { Address = _filterIps.RemoteTestIpV4s[i] };
        }

        return replyEndPoint;
    }
}