using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Sockets;
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
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Establishing a new TCP to the Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));

            if (proxyConnector is { IsEnabled: true })
                // The proxy connector creates the socket internally, so the packet-channel kernel-buffer
                // override cannot reach it; proxied packet channels keep the factory-default buffer and
                // whatever throughput ceiling comes with it.
                tcpClient = await proxyConnector
                    .ConnectAsync(socketFactory, tcpEndPoint, onConnectAttempt, cancellationToken).Vhc();
            else {
                // Packet mode multiplexes every inner TCP flow over this one outer connection, so it can
                // need a larger BDP window than proxy/control and direct/split-flow sockets. Apply the
                // override before ConnectAsync because TCP window scaling is negotiated in the handshake.
                var socketOptions = isTcpPacketChannel && tcpPacketChannelKernelBufferSize != null
                    ? new TcpClientOptions {
                        SendBufferSize = tcpPacketChannelKernelBufferSize.Value.Send,
                        ReceiveBufferSize = tcpPacketChannelKernelBufferSize.Value.Receive
                    }
                    : null;
                tcpClient = socketFactory.CreateTcpClient(tcpEndPoint, socketOptions);
                await tcpClient.ConnectAsync(tcpEndPoint, cancellationToken).Vhc();
            }

            return await AuthenticateTls(connectionId, tcpClient, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            if (proxyConnector is { IsEnabled: true } && tcpClient != null)
                proxyConnector.RecordFailed(tcpClient, ex);

            tcpClient?.Dispose();
            throw;
        }
    }

    private async Task<IStreamConnection> AuthenticateTls(string connectionId, TcpClient tcpClient,
        CancellationToken cancellationToken)
    {
        var sslStream = new SslStream(tcpClient.GetStream(), true, certificateValidationCallback);
        try {
            var hostName = vpnEndPoint.HostName;
            VhLogger.Instance.LogDebug(GeneralEventId.Request, "TLS Authenticating... HostName: {HostName}",
                VhLogger.FormatHostName(hostName));

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = hostName,
                EnabledSslProtocols = SslProtocols.None // auto
            }, cancellationToken).Vhc();

            var tcpConnection = new TcpStreamConnection(tcpClient, sslStream,
                connectionId: connectionId, connectionName: "tunnel", isServer: false);
            return tcpConnection;
        }
        catch {
            await sslStream.TryDisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose for TCP factory
    }
}
