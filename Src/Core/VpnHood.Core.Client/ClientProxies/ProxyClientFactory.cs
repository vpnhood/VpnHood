using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies;
using VpnHood.Core.Proxies.HttpProxyClients;
using VpnHood.Core.Proxies.Socks4ProxyClients;
using VpnHood.Core.Proxies.Socks5ProxyClients;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.ClientProxies;

public static class ProxyClientFactory
{
    public static async Task<IPAddress> GetIpAddress(string host)
    {
        // try parse the proxy address
        if (IPAddress.TryParse(host, out var ipAddress))
            return ipAddress;

        var entry = await Dns.GetHostEntryAsync(host).Vhc();
        if (entry.AddressList.Length == 0)
            throw new Exception("Failed to resolve proxy server address.");

        // select a random address if multiple addresses are returned
        ipAddress = entry.AddressList[Random.Shared.Next(entry.AddressList.Length)];
        return ipAddress;
    }

    public static async Task<IProxyClient> CreateProxyClient(ProxyServerEndPoint proxyEndPoint)
    {
        var serverIp = await GetIpAddress(proxyEndPoint.Host).Vhc();
        var serverEp = new IPEndPoint(serverIp, proxyEndPoint.Port);

        return proxyEndPoint.Type switch {
            ProxyServerType.Socks5 => new Socks5ProxyClient(new Socks5ProxyClientOptions {
                ProxyEndPoint = serverEp,
                Password = proxyEndPoint.Password,
                Username = proxyEndPoint.Username
            }),
            ProxyServerType.Socks4 => new Socks4ProxyClient(new Socks4ProxyClientOptions {
                ProxyEndPoint = serverEp,
                UserName = proxyEndPoint.Username
            }),
            ProxyServerType.Https or ProxyServerType.Http => new HttpProxyClient(new HttpProxyOptions {
                ProxyEndPoint = serverEp,
                Username = proxyEndPoint.Username,
                Password = proxyEndPoint.Password,
                AllowInvalidCertificates = true,
                ProxyHost = proxyEndPoint.Host,
                UseTls = proxyEndPoint.Type == ProxyServerType.Https
            }),
            _ => throw new NotSupportedException($"Proxy type {proxyEndPoint.Type} is not supported.")
        };
    }
}