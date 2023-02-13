using PacketDotNet;
using System.Net;
using VpnHood.Common.Net;

namespace VpnHood.Server;

public interface INetFilter
{
    public IpRange[] BlockedIpRanges { get; set; }
    public IPPacket? ProcessRequest(IPPacket ipPacket);
    public IPEndPoint? ProcessRequest(ProtocolType protocol, IPEndPoint requestEndPoint);
    public IPPacket ProcessReply(IPPacket ipPacket);
}