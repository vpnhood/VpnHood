using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public interface IIpFilter : IDisposable
{
    FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint);
}