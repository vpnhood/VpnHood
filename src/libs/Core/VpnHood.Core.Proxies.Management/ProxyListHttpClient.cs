using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management;

// The HTTP client of a list's downloads. With a socket factory, as under the kill switch's block, which lets only the
// VPN's own traffic out and leaves the system's resolver, asking from a process of its own, without an answer: over its
// sockets, straight to the list's host, its name asked of the network's resolvers over them, as the server search does
// there. Otherwise the system's client, with the platform's own resolver, handler and routing
internal static class ProxyListHttpClient
{
    private static readonly TimeSpan ResolverTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AddressConnectTimeout = TimeSpan.FromSeconds(5);

    // timeout: the download's whole time, or null for HttpClient's own
    public static HttpClient Create(ISocketFactory? socketFactory, TimeSpan? timeout)
    {
        var httpClient = socketFactory is null
            ? new HttpClient()
            : new HttpClient(new SocketsHttpHandler {
                ConnectCallback = (context, cancellationToken) =>
                    Connect(socketFactory, context.DnsEndPoint, cancellationToken),

                // no system proxy, whose address the callback would be given in the host's place
                UseProxy = false,

                // no certificate downloaded for the list host's chain: the download would go around these sockets
                SslOptions = new SslClientAuthenticationOptions {
                    CertificateChainPolicy = new X509ChainPolicy {
                        DisableCertificateDownloads = true,
                        RevocationMode = X509RevocationMode.NoCheck
                    }
                }
            });

        if (timeout is { } value)
            httpClient.Timeout = value;

        return httpClient;
    }

    // each of the name's addresses in turn, each for a few seconds at most, until one connects
    private static async ValueTask<Stream> Connect(ISocketFactory socketFactory, DnsEndPoint endPoint,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses = IPAddress.TryParse(endPoint.Host, out var address)
            ? [address]
            : await DnsUtils.GetHostAddressesByNetworkResolvers(endPoint.Host, socketFactory, ResolverTimeout,
                cancellationToken).Vhc();

        Exception? lastError = null;
        foreach (var ipAddress in addresses) {
            var ipEndPoint = new IPEndPoint(ipAddress, endPoint.Port);
            TcpClient? tcpClient = null;
            try {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(AddressConnectTimeout);
                tcpClient = socketFactory.CreateTcpClient(ipEndPoint);
                await tcpClient.ConnectAsync(ipEndPoint, connectCts.Token).Vhc();
                return tcpClient.GetStream();
            }
            catch (Exception ex) {
                tcpClient?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                    throw;

                lastError = ex;
            }
        }

        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }
}
