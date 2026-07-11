using System.Net;

namespace VpnHood.Core.Toolkit.Net;

// ReSharper disable once InconsistentNaming
public static class IPEndPointExtensions
{
    extension(IPEndPoint ipEndPoint)
    {
        public IpEndPointValue ToValue() => new(ipEndPoint.Address, ipEndPoint.Port);
    }
}