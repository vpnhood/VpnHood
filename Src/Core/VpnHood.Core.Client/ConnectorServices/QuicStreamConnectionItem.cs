using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Client.ConnectorServices;

/// <summary>
/// Represents a single QUIC connection in the <see cref="QuicStreamConnectionFactory"/> pool,
/// tracking the number of active and lifetime streams opened on it.
/// </summary>
/// <remarks>
/// Instances are created by <see cref="QuicStreamConnectionFactory"/> and should not be used directly.
/// The underlying <see cref="QuicConnection"/> is established lazily on the first
/// Once <see cref="IsRetired"/> becomes true (lifetime stream limit reached), no new streams will be
/// opened on this connection. The factory disposes it as soon as <see cref="ActiveStreamCount"/> drops to zero.
/// Each opened stream notifies the factory via the <c>onDisposed</c> callback passed to
/// <see cref="VpnHood.Core.Tunneling.Connections.QuicStreamConnection"/> so the active count is decremented
/// and retirement cleanup is triggered automatically.
/// </remarks>
internal class QuicStreamConnectionItem(QuicStreamConnectionFactory factory) : IAsyncDisposable
{
    private QuicConnection? _connection;
    private readonly AsyncLock _connectLock = new();

    public int ActiveStreamCount { get; set; } = 1;
    public int TotalStreamCount { get; set; } = 1;
    public QuicConnection Connection => 
        _connection ?? throw new InvalidOperationException("QUIC connection has not been established yet.");

    public bool IsRetired => TotalStreamCount >= factory.MaxLifetimeStreamsPerConnection;

    public bool CanOpenStream =>
        !IsRetired &&
        ActiveStreamCount < factory.MaxStreamsPerConnection;

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync().Vhc();
    }

    public async Task<QuicStream> OpenStreamAsync(
        VpnEndPoint vpnEndPoint,
        RemoteCertificateValidationCallback certificateValidationCallback,
        CancellationToken cancellationToken)
    {
        using var asyncLock = await _connectLock.LockAsync(cancellationToken).Vhc();
        _connection ??= await ConnectAsync(vpnEndPoint, certificateValidationCallback, cancellationToken).Vhc();
        return await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).Vhc();
    }

    private async Task<QuicConnection> ConnectAsync(
        VpnEndPoint vpnEndPoint,
        RemoteCertificateValidationCallback certificateValidationCallback,
        CancellationToken cancellationToken)
    {
        var quicEndPoint = factory.QuicEndPoint
                           ?? throw new InvalidOperationException("QuicEndPoint has not been set.");

        VhLogger.Instance.LogDebug(GeneralEventId.Request,
            "Establishing a new QUIC connection to the Server... EndPoint: {EndPoint}",
            VhLogger.Format(quicEndPoint));

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

        return await QuicConnection.ConnectAsync(options, cancellationToken).Vhc();
    }
}