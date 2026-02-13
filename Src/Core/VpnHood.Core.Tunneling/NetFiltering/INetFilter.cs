using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.NetFiltering;

public interface INetFilter
{
    IpRangeOrderedList BlockedIpRanges { get; set; }
    IpPacket? ProcessRequest(IpPacket ipPacket);
    IPEndPoint? ProcessRequest(IpProtocol protocol, IPEndPoint requestEndPoint);
    IpPacket? ProcessReply(IpPacket ipPacket);
}

public enum FilterAction
{
    Default, 
    Block,
    Exclude,
    Include
}

public interface IClientNetFilter
{
    FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint);
    FilterAction Process(string domainName);
}

public interface INetMapper
{
    bool ToHost(IpProtocol protocol, IpEndPointValue requestEndPoint, out IpEndPointValue newEndPoint);
    bool FromHost(IpProtocol protocol, IpEndPointValue requestEndPoint, out IpEndPointValue newEndPoint);
}
