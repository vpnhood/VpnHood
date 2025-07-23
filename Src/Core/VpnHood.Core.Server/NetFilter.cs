using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Server;

public class NetFilter : INetFilter
{
    private readonly IpRangeOrderedList _loopbackIpRange = IpNetwork.LoopbackNetworks.ToIpRanges();
    private IpRangeOrderedList _blockedIpRanges = new([]);
    public bool BlockBroadcast { get; set; } = true;
    public bool BlockMulticast { get; set; } = true;
    public bool BlockLoopback { get; set; } = true;

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
        if (BlockBroadcast && ipAddress.IsBroadcast())
            return true;

        if (BlockMulticast && ipAddress.IsMulticast())
            return true;

        if (BlockLoopback && ipAddress.IsLoopback())
            return true;

        return BlockedIpRanges.IsInRange(ipAddress);
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IpPacket? ProcessRequest(IpPacket ipPacket)
    {
        return IsIpAddressBlocked(ipPacket.DestinationAddress) ? null : ipPacket;
    }

    // ReSharper disable once ReturnTypeCanBeNotNullable
    public virtual IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint)
    {
        return IsIpAddressBlocked(requestEndPoint.Address) ? null : requestEndPoint;
    }

    public virtual IpPacket ProcessReply(IpPacket ipPacket)
    {
        return ipPacket;
    }
}