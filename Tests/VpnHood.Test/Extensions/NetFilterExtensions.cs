using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Extensions;

public static class NetFilterExtensions
{
    extension(NetFilter netFilter)
    {
        public bool IsIpBlocked(string ipAddress)
        {
            return netFilter.IsIpBlocked(IPAddress.Parse(ipAddress));
        }

        public bool IsIpBlocked(IPAddress ipAddress)
        {
            return netFilter.IpFilter.IsIpBlocked(ipAddress);
        }

        public bool IsIpIncluded(string ipAddress)
        {
            return netFilter.IsIpIncluded(IPAddress.Parse(ipAddress));
        }

        public bool IsIpIncluded(IPAddress ipAddress)
        {
            return netFilter.IpFilter.IsIpIncluded(ipAddress);
        }
    }

    extension(IIpFilter? ipFilter)
    {
        public bool IsIpBlocked(string ipAddress)
        {
            return ipFilter.IsIpBlocked(IPAddress.Parse(ipAddress));
        }

        public bool IsIpBlocked(IPAddress ipAddress)
        {
            var result = ipFilter?.Process(IpProtocol.Tcp,
                new IpEndPointValue(ipAddress, 443));

            return result == FilterAction.Block;
        }

        public bool IsIpIncluded(string ipAddress)
        {
            return ipFilter.IsIpIncluded(IPAddress.Parse(ipAddress));
        }

        public bool IsIpIncluded(IPAddress ipAddress)
        {
            var result = ipFilter?.Process(IpProtocol.Tcp,
                new IpEndPointValue(ipAddress, 443));

            return result == FilterAction.Include;
        }
    }
}
