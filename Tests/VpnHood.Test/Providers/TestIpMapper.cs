using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Providers;

public class TestIpMapper(TestIps filterIps) : IIpMapper
{
    public bool ToHost(IpProtocol protocol, IpEndPointValue hostEndPoint, out IpEndPointValue newEndPoint)
    {
        if (hostEndPoint.Address.Equals(filterIps.RemoteTestIpV4)) {
            newEndPoint = hostEndPoint with { Address = filterIps.LocalTestIpV4 };
            return true;
        }

        // map IPv6
        if (hostEndPoint.Address.Equals(filterIps.RemoteTestIpV6)) {
            newEndPoint = hostEndPoint with { Address = filterIps.LocalTestIpV6 };
        }

        // map IPv4s
        for (var i = 0; i < filterIps.RemoteTestIpV4List.Count; i++) {
            if (hostEndPoint.Address.Equals(filterIps.RemoteTestIpV4List[i])){
                newEndPoint =  hostEndPoint with { Address = filterIps.LocalTestIpV4List[i] };
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

        // map IPv4 
        if (hostEndPoint.Address.Equals(filterIps.LocalTestIpV4)) {
            newEndPoint = hostEndPoint with { Address = filterIps.RemoteTestIpV4 };
            return  true;
        }

        // map IPv4s
        for (var i = 0; i < filterIps.LocalTestIpV4List.Count; i++) {
            if (hostEndPoint.Address.Equals(filterIps.LocalTestIpV4List[i])) {
                newEndPoint = hostEndPoint with { Address = filterIps.RemoteTestIpV4List[i] };
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