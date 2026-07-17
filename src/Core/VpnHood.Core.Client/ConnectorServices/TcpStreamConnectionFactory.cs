using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.Core.Client.ConnectorServices;

internal class TcpStreamConnectionFactory(
    ISocketFactory socketFactory,
    IProxyConnector? proxyConnector,
    VpnEndPoint vpnEndPoint,
    RemoteCertificateValidationCallback certificateValidationCallback)
    : IDisposable
{
    public async Task<IStreamConnection> CreateConnection(string connectionId, Action? onConnectAttempt,
        CancellationToken cancellationToken)
    {
        var tcpEndPoint = vpnEndPoint.TcpEndPoint;

        TcpClient? tcpClient = null;
        try {
            VhLogger.Instance.LogDebug(GeneralEventId.Request,
                "Establishing a new TCP to the Server... EndPoint: {EndPoint}", VhLogger.Format(tcpEndPoint));

            if (proxyConnector is { IsEnabled: true })
                tcpClient = await proxyConnector
                    .ConnectAsync(socketFactory, tcpEndPoint, onConnectAttempt, cancellationToken).Vhc();
            else {
                tcpClient = socketFactory.CreateTcpClient(tcpEndPoint);
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
            await sslStream.SafeDisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        // No persistent resources to dispose for TCP factory
    }
}