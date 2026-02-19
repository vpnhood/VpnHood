using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Providers;

public class TestIps 
{
    public IPAddress RemoteTestIpV6 { get; } = IPAddress.Parse("2001:db8::1");
    public IPAddress LocalTestIpV6 => IPAddress.IPv6Loopback;
    public IPAddress RemoteTestIpV4 { get; } = IPAddress.Parse("198.18.11.1");
    public IPAddress LocalTestIpV4 => IPAddress.Loopback;
    public IReadOnlyList<IPAddress> RemoteTestIpV4List { get; } // additional ipV4
    public IReadOnlyList<IPAddress> LocalTestIpV4List { get; } // additional ipV4
    public IPAddress InvalidRemoteTestIpV4 { get; } = IPAddress.Parse("198.18.12.1");
    public IPAddress LocalBlockedClientIpAddress { get; }
    public IPAddress LocalBlockedServerIpAddress { get; }

    public IReadOnlyList<IPAddress> AllRemoteTestIps {
        get => new[] {RemoteTestIpV6, RemoteTestIpV4, InvalidRemoteTestIpV4 }
            .Concat(RemoteTestIpV4List)
            .ToList();
    }

    public TestIps()
    {
        // remote test addresses
        var remoteIpV4S = new List<IPAddress>();
        var startIp = IPAddress.Parse("198.18.11.2");
        for (var i = 0; i < 100; i++) {
            startIp = IPAddressUtil.Increment(startIp);
            remoteIpV4S.Add(IPAddressUtil.Increment(startIp));
        }
        RemoteTestIpV4List = remoteIpV4S;

        // local test addresses
        var localIpV4S = new List<IPAddress>();
        startIp = IPAddress.Loopback;
        for (var i = 0; i < 100; i++) {
            startIp = IPAddressUtil.Increment(startIp);
            localIpV4S.Add(IPAddressUtil.Increment(startIp));
        }
        LocalTestIpV4List = localIpV4S;
        LocalBlockedServerIpAddress = localIpV4S[^1];
        LocalBlockedClientIpAddress = localIpV4S[^2];
    }

    public IPAddress MapToRemote(IPAddress address)
    {
        if (address.Equals(LocalTestIpV4))
            return RemoteTestIpV4;

        if (address.Equals(LocalTestIpV6))
            return RemoteTestIpV6;

        for (var i = 0; i < LocalTestIpV4List.Count; i++) {
            if (LocalTestIpV4List[i].Equals(address))
                return RemoteTestIpV4List[i];
        }

        return address;
    }

    public IPEndPoint MapToRemote(IPEndPoint ipEndPoint)
    {
        var remoteAddress = MapToRemote(ipEndPoint.Address);
        return new IPEndPoint(remoteAddress, ipEndPoint.Port);
    }

    public Uri MapToRemote(Uri url)
    {
        if (!IPAddress.TryParse(url.Host, out var ipAddress))
            throw new ArgumentException($"URI host '{url.Host}' is not a valid IP address.", nameof(url));

        var remoteAddress = MapToRemote(ipAddress);
        var builder = new UriBuilder(url) {
            Host = remoteAddress.ToString()
        };
        return builder.Uri;
    }

}