using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Providers;

public class TestIpMapper(TestIps filterIps) : IIpMapper
{
    public bool ToHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint)
    {
        // map IPv6
        if (hostEndPoint.Address.Equals(filterIps.RemoteTestIpV6)) {
            newEndPoint = hostEndPoint with { Address = filterIps.LocalTestIpV6 };
        }

        // map IPv4s
        for (var i = 0; i < filterIps.RemoteTestIps.Count; i++) {
            if (hostEndPoint.Address.Equals(filterIps.RemoteTestIps[i])){
                newEndPoint =  hostEndPoint with { Address = filterIps.LocalTestIps[i] };
                return  true;
            }
        }

        newEndPoint = default;
        return false;
    }

    public bool FromHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint)
    {
        if (hostEndPoint.Address.Equals(filterIps.LocalTestIpV6)) {
            newEndPoint =  hostEndPoint with { Address = filterIps.RemoteTestIpV6 };
            return  true;
        }

        // map IPv4s
        for (var i = 0; i < filterIps.LocalTestIps.Count; i++) {
            if (hostEndPoint.Address.Equals(filterIps.LocalTestIps[i])) {
                newEndPoint = hostEndPoint with { Address = filterIps.RemoteTestIps[i] };
                return true;
            }
        }

        newEndPoint = default;
        return false;
    }

    public void Dispose()
    {
    }
}