using System.Net;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Proxies;
using VpnHood.Net.Proxies.HttpProxyClients;
using VpnHood.Net.Proxies.Socks4ProxyClients;
using VpnHood.Net.Proxies.Socks5ProxyClients;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management;

/// <summary>
/// Provides the clients of <c>VpnHood.Net.Proxies</c> for proxy endpoints.
/// </summary>
internal static class ProxyClientFactory
{
    /// <summary>Gets a proxy host's IP address: the host itself when it is one, or else one its DNS gives.</summary>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the address, picked at random where DNS gives several.</returns>
    /// <exception cref="Exception">DNS gave no address.</exception>
    /// <exception cref="System.Net.Sockets.SocketException">DNS could not resolve the host.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>
    /// Creates the client of a proxy endpoint's protocol, for the endpoint's address and credentials.
    /// </summary>
    /// <param name="proxyEndPoint">The endpoint; its host is resolved first, as <see cref="GetIpAddress"/> does.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the client.</returns>
    /// <remarks>
    /// A SOCKS4 client takes the user name alone. An HTTPS client accepts any certificate of the proxy, and names the
    /// endpoint's host in its TLS handshake.
    /// </remarks>
    /// <exception cref="NotSupportedException">The endpoint's protocol is not one of <see cref="ProxyProtocol"/>'s.</exception>
    public static async Task<IProxyClient> CreateProxyClient(ProxyEndPoint proxyEndPoint,
        CancellationToken cancellationToken)
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