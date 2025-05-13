using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Server.Abstractions;

public interface INetFilter
{
    public IpRangeOrderedList BlockedIpRanges { get; set; }
    public IpPacket? ProcessRequest(IpPacket ipPacket);
    public IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint);
    public IpPacket ProcessReply(IpPacket ipPacket);
}