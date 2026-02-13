using System.Net;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling.NetFiltering;

namespace VpnHood.Test.Providers;

public class TestNetFilterIps 
{
    public IPAddress RemoteTestIpV6 { get; } = IPAddress.Parse("2001:db8::1");
    public IPAddress LocalTestIpV6 => IPAddress.IPv6Loopback;
    public IPAddress RemoteTestIpV4 { get; } = IPAddress.Parse("198.18.11.1");
    public IPAddress LocalTestIpV4 => IPAddress.Loopback;

    public IReadOnlyList<IPAddress> BlockedIpAddresses => [IPAddress.IPv6Loopback];
    public IReadOnlyList<IPAddress> RemoteTestIpV4s { get; } // additional ipV4
    public IReadOnlyList<IPAddress> LocalTestIpV4s { get; } // additional ipV4

    public TestNetFilterIps()
    {
        // remote test addresses
        var removeIpV4s = new List<IPAddress>();
        var startIp = IPAddress.Parse("198.18.11.2");
        for (var i = 0; i < 100; i++) {
            startIp = IPAddressUtil.Increment(startIp);
            removeIpV4s.Add(IPAddressUtil.Increment(startIp));
        }
        RemoteTestIpV4s = removeIpV4s;

        // local test addresses
        var localIpV4s = new List<IPAddress>();
        startIp = IPAddress.Loopback;
        for (var i = 0; i < 100; i++) {
            startIp = IPAddressUtil.Increment(startIp);
            localIpV4s.Add(IPAddressUtil.Increment(startIp));
        }
        LocalTestIpV4s = localIpV4s;
    }

}