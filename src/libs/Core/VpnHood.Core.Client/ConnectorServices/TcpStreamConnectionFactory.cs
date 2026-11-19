using VpnHood.Net.Toolkit.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

internal class TcpStreamConnectionFactory(
    ISocketFactory socketFactory,
    IProxyConnector? proxyConnector,
    VpnEndPoint vpnEndPoint,
    RemoteCertificateValidationCallback certificateValidationCallback,
    TransferBufferSize? tcpPacketChannelKernelBufferSize)
    : IDisposable
{
    public async Task<IStreamConnection> CreateConnection(string connectionId, bool isTcpPacketChannel,
        Action? onConnectAttempt, CancellationToken cancellationToken)
    {
        var tcpEndPoint = vpnEndPoint.TcpEndPoint;

        TcpClient? tcpClient = null;
        Stream? stream = null;
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Establishing a new TCP to the Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));

            // Through the proxy connector whenever there is one, a managed list with no endpoint yet too, which waits for
            // its first download or fails: never straight to the server once the user's proxies are on
            if (proxyConnector != null) {
                // The proxy connector creates the socket internally, so the packet-channel kernel-buffer
                // override cannot reach it; proxied packet channels keep the factory-default buffer and
                // whatever throughput ceiling comes with it. Its stream is the one to the server, an HTTPS proxy's
                // TLS session too, which the socket's own stream would pass by
                var proxyConnection = await proxyConnector
                    .ConnectAsync(socketFactory, tcpEndPoint, vpnEndPoint.HostName, certificateValidationCallback,
                        onConnectAttempt, cancellationToken)
                    .Vhc();

                tcpClient = proxyConnection.TcpClient;
                stream = proxyConnection.Stream;
            }
            else {
                // Packet mode multiplexes every inner TCP flow over this one outer connection, so its
                // sizing is decided here rather than inherited from the factory default, which exists to
                // bound the many per-flow direct/split sockets. Pass the options whenever this is a packet
                // channel, including when the size is null — null is the deliberate "leave it to the OS"
                // choice, and only a non-null options object can express it. Applied before ConnectAsync
                // because TCP window scaling is negotiated in the handshake.
                var socketOptions = isTcpPacketChannel
                    ? new TcpClientOptions { KernelBufferSize = tcpPacketChannelKernelBufferSize }
                    : null;
                tcpClient = socketFactory.CreateTcpClient(tcpEndPoint, socketOptions);
                await tcpClient.ConnectAsync(tcpEndPoint, cancellationToken).Vhc();
                stream = tcpClient.GetStream();
            }

            return await AuthenticateTls(connectionId, tcpClient, stream, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            if (proxyConnector != null && tcpClient != null)
                proxyConnector.RecordFailed(tcpClient, ex);

            stream?.Dispose();
            tcpClient?.Dispose();
            throw;
        }
    }

    private async Task<IStreamConnection> AuthenticateTls(string connectionId, TcpClient tcpClient, Stream stream,
        CancellationToken cancellationToken)
    {
        // An HTTPS proxy's TLS session under it is closed with it: the socket's own stream stays for the socket's close
        var sslStream = new SslStream(stream, leaveInnerStreamOpen: stream is NetworkStream,
            certificateValidationCallback);

        try {
            var hostName = vpnEndPoint.HostName;
            VhLogger.Instance.LogDebug(GeneralEventId.Request, "TLS Authenticating... HostName: {HostName}",
                VhLogger.FormatHostName(hostName));

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = hostName,
                EnabledSslProtocols = SslProtocols.None, // auto
                // http/1.1 alone, as an HTTP library offers it: a hello without ALPN stands out, and a reverse proxy
                // would take an h2 offer, which the tunnel does not speak. .NET drops it on Android below 10.
                ApplicationProtocols = [SslApplicationProtocol.Http11],
                // No certificate is downloaded for the server's chain: a certificate that someone on the way shows, a
                // proxy too, can name any address, which the device would fetch outside the tunnel. Our server sends
                // its chain itself
                CertificateChainPolicy = new X509ChainPolicy {
                    DisableCertificateDownloads = true,
                    RevocationMode = X509RevocationMode.NoCheck
                }
            }, cancellationToken).Vhc();

            var tcpConnection = new TcpStreamConnection(tcpClient, sslStream,
                connectionId: connectionId, connectionName: "tunnel", isServer: false);
            return tcpConnection;
        }
        catch {
            await sslStream.TryDisposeAsync().Vhc();
            throw;
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose for TCP factory
    }
}
