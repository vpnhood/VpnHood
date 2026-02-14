using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public interface IIpMapper
{
    bool ToHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint);
    bool FromHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint);
}