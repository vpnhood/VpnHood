using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Server.Abstractions;

public interface INetFilter
{
    IpRangeOrderedList BlockedIpRanges { get; set; }
    IpPacket? ProcessRequest(IpPacket ipPacket);
    IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint);
    IpPacket ProcessReply(IpPacket ipPacket);
}