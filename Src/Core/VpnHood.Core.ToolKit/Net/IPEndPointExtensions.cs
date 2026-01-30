using System.Net;

namespace VpnHood.Core.Toolkit.Net;

public static class IPEndPointExtensions
{
    extension(IPEndPoint ipEndPoint)
    {
        public IpEndPointValue ToIpEndPointValue() => new(ipEndPoint.Address, ipEndPoint.Port);
    }
}