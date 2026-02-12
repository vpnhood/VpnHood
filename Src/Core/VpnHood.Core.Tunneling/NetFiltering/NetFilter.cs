using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.NetFiltering;

public class NetFilter : INetFilter
{
    public bool BlockBroadcast { get; set; } = true;
    public bool BlockMulticast { get; set; } = true;
    public bool BlockLoopback { get; set; } = true;

    public IpRangeOrderedList BlockedIpRanges { get; set; } = new();

    protected virtual bool IsIpAddressBlocked(IPAddress ipAddress)
    {
        // broadcast
        if (BlockBroadcast && ipAddress.IsBroadcast()) {
            if (VhLogger.MinLogLevel > LogLevel.Trace)
                VhLogger.Instance.LogTrace(
                    "A broadcast address has been detected. Address: {Address}", VhLogger.Format(ipAddress));
            return true;
        }

        // multicast
        if (BlockMulticast && ipAddress.IsMulticast()) {
            if (VhLogger.MinLogLevel > LogLevel.Trace)
                VhLogger.Instance.LogTrace(
                    "A multicast address has been detected. Address: {Address}", VhLogger.Format(ipAddress));
            return true;
        }

        // loopback
        if (BlockLoopback && ipAddress.IsLoopback()) {
            if (VhLogger.MinLogLevel > LogLevel.Trace)
                VhLogger.Instance.LogTrace(
                    "A loopback address has been detected. Address: {Address}", VhLogger.Format(ipAddress));
            return true;
        }

        // blocked IP ranges
        return BlockedIpRanges.Count == 0 || BlockedIpRanges.IsInRange(ipAddress);
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