using System.Net;
using PacketDotNet;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Server;

public class NetFilter : INetFilter
{
    private readonly IpRangeOrderedList _loopbackIpRange = IpNetwork.LoopbackNetworks.ToIpRanges();
    private IpRangeOrderedList _blockedIpRanges = new([]);

    public NetFilter()
    {
        BlockedIpRanges = _loopbackIpRange;
    }

    public IpRangeOrderedList BlockedIpRanges {
        get => _blockedIpRanges;
        set => _blockedIpRanges = _loopbackIpRange.Union(value);
    }

    private bool IsIpAddressBlocked(IPAddress ipAddress)
    {
        return BlockedIpRanges.IsInRange(ipAddress);
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IPPacket? ProcessRequest(IPPacket ipPacket)
    {
        return IsIpAddressBlocked(ipPacket.DestinationAddress) ? null : ipPacket;
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IPEndPoint? ProcessRequest(ProtocolType protocol, IPEndPoint requestEndPoint)
    {
        return IsIpAddressBlocked(requestEndPoint.Address) ? null : requestEndPoint;
    }

    public virtual IPPacket ProcessReply(IPPacket ipPacket)
    {
        return ipPacket;
    }
}