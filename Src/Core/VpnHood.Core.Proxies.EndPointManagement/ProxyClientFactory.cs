using System.Net;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.HttpProxyClients;
using VpnHood.Core.Proxies.Socks4ProxyClients;
using VpnHood.Core.Proxies.Socks5ProxyClients;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement;

public static class ProxyClientFactory
{
    public static async Task<IPAddress> GetIpAddress(string host, CancellationToken cancellationToken)
    {
        // try parse the proxy address
        if (IPAddress.TryParse(host, out var ipAddress))
            return ipAddress;

        var entry = await Dns.GetHostEntryAsync(host, cancellationToken).Vhc();
        if (entry.AddressList.Length == 0)
            throw new Exception("Failed to resolve proxy server address.");

        // select a random address if multiple addresses are returned
        ipAddress = entry.AddressList[Random.Shared.Next(entry.AddressList.Length)];
        return ipAddress;
    }

    public static async Task<IProxyClient> CreateProxyClient(ProxyEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        var serverIp = await GetIpAddress(proxyEndPoint.Host, cancellationToken).Vhc();
        var serverEp = new IPEndPoint(serverIp, proxyEndPoint.Port);

        return proxyEndPoint.Protocol switch {
            ProxyProtocol.Socks5 => new Socks5ProxyClient(new Socks5ProxyClientOptions {
                ProxyEndPoint = serverEp,
                Password = proxyEndPoint.Password,
                Username = proxyEndPoint.Username
            }),
            ProxyProtocol.Socks4 => new Socks4ProxyClient(new Socks4ProxyClientOptions {
                ProxyEndPoint = serverEp,
                UserName = proxyEndPoint.Username
            }),
            ProxyProtocol.Https or ProxyProtocol.Http => new HttpProxyClient(new HttpProxyClientOptions {
                ProxyEndPoint = serverEp,
                Username = proxyEndPoint.Username,
                Password = proxyEndPoint.Password,
                AllowInvalidCertificates = true,
                ProxyHost = proxyEndPoint.Host,
                UseTls = proxyEndPoint.Protocol == ProxyProtocol.Https
            }),
            _ => throw new NotSupportedException($"Proxy type {proxyEndPoint.Protocol} is not supported.")
        };
    }
}