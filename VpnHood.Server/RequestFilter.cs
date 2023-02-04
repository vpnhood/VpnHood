using System;
using System.Net;
using PacketDotNet;
using VpnHood.Common.Net;

namespace VpnHood.Server;

public class RequestFilter : IRequestFilter
{
    private IpRange[] _sortedBlockedIpRanges = Array.Empty<IpRange>();

    public IpRange[] BlockedIpRanges
    {
        get => _sortedBlockedIpRanges;
        set => _sortedBlockedIpRanges = IpRange.Sort(value);
    }

    public virtual bool IsIpAddressBlocked(IPAddress ipAddress)
    {
        return BlockedIpRanges.Length > 0 && IpRange.IsInRangeFast(BlockedIpRanges, ipAddress);
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IPPacket? Process(IPPacket ipPacket)
    {
        return IsIpAddressBlocked(ipPacket.DestinationAddress) ? null : ipPacket;
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IPEndPoint? Process(ProtocolType protocolType, IPEndPoint requestEndPoint)
    {
        return IsIpAddressBlocked(requestEndPoint.Address) ? null : requestEndPoint;
    }
}