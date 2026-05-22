using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Client.ConnectorServices;

internal class QuicStreamConnectionFactory(
    VpnEndPoint vpnEndPoint,
    RemoteCertificateValidationCallback certificateValidationCallback)
    : IAsyncDisposable
{
    private QuicConnection? _quicConnection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public IPEndPoint? QuicEndPoint { get; set; }

    public async Task<IStreamConnection> CreateConnection(string connectionId, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).Vhc();
        try {
            var quicConnection = await GetOrCreateConnection(cancellationToken).Vhc();
            var stream = await quicConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).Vhc();

            return new QuicStreamConnection(stream,
                localEndPoint: quicConnection.LocalEndPoint,
                remoteEndPoint: quicConnection.RemoteEndPoint,
                connectionName: "tunnel",
                isServer: false,
                connectionId: connectionId);
        }
        finally {
            _connectionLock.Release();
        }
    }

    private async Task<QuicConnection> GetOrCreateConnection(CancellationToken cancellationToken)
    {
        if (_quicConnection != null)
            return _quicConnection;

        var quicEndPoint = QuicEndPoint ?? throw new InvalidOperationException("QuicEndPoint has not been set.");
        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Establishing a new QUIC connection to the Server... EndPoint: {EndPoint}", VhLogger.Format(quicEndPoint));

        var options = new QuicClientConnectionOptions {
            RemoteEndPoint = quicEndPoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                RemoteCertificateValidationCallback = certificateValidationCallback,
                EnabledSslProtocols = SslProtocols.Tls13,
                TargetHost = vpnEndPoint.HostName,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption
            }
        };

        _quicConnection = await QuicConnection.ConnectAsync(options, cancellationToken).Vhc();
        return _quicConnection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_quicConnection != null) {
            try { await _quicConnection.DisposeAsync().Vhc(); } catch { /* ignore */ }
            _quicConnection = null;
        }
        _connectionLock.Dispose();
    }
}
