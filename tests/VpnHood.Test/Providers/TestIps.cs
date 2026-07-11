using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Providers;

public class TestIps 
{
    public IPAddress RemoteTestIpV6 { get; } = IPAddress.Parse("2001:db8::1");
    public IPAddress LocalTestIpV6 => IPAddress.IPv6Loopback;
    public IReadOnlyList<IPAddress> RemoteTestIps { get; } 
    public IReadOnlyList<IPAddress> LocalTestIps { get; }
    public IPAddress RemoteInvalidTestIpV4 { get; } = IPAddress.Parse("198.18.12.1");
    public IPAddress LocalBlockedClientIpAddress { get; }
    public IPAddress LocalBlockedServerIpAddress { get; }

    public IReadOnlyList<IPAddress> AllRemoteTestIps {
        get => RemoteTestIps
            .Append(RemoteTestIpV6)
            .Append(RemoteInvalidTestIpV4)
            .Concat(RemoteTestIps)
            .ToList();
    }

    public TestIps()
    {
        // test addresses
        var remoteIpV4S = new List<IPAddress>();
        var localIpV4S = new List<IPAddress>();
        
        var remoteStartIp = IPAddress.Parse("198.18.11.1");
        var localStartIp = IPAddressUtil.Decrement(IPAddress.Loopback);

        for (var i = 0; i < 100; i++) {
            remoteStartIp = IPAddressUtil.Increment(remoteStartIp);
            remoteIpV4S.Add(remoteStartIp);

            localStartIp = IPAddressUtil.Increment(localStartIp);
            localIpV4S.Add(localStartIp);
        }

        // server blocked ip
        remoteStartIp = IPAddressUtil.Increment(remoteStartIp);
        remoteIpV4S.Add(remoteStartIp);
        localStartIp = IPAddressUtil.Increment(localStartIp);
        localIpV4S.Add(localStartIp);
        LocalBlockedServerIpAddress = localStartIp;

        // client blocked ip
        remoteStartIp = IPAddressUtil.Increment(remoteStartIp);
        remoteIpV4S.Add(remoteStartIp);
        localStartIp = IPAddressUtil.Increment(localStartIp);
        localIpV4S.Add(localStartIp);
        LocalBlockedClientIpAddress = localStartIp;

        // map remote test ips to local test ips
        LocalTestIps = localIpV4S;
        RemoteTestIps = remoteIpV4S;

    }

    public IPAddress MapToRemote(IPAddress address)
    {
        if (address.Equals(LocalTestIpV6))
            return RemoteTestIpV6;

        for (var i = 0; i < LocalTestIps.Count; i++) {
            if (LocalTestIps[i].Equals(address))
                return RemoteTestIps[i];
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