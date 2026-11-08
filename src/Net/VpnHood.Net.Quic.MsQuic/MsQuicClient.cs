using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Quic.MsQuic;

/// <summary>
/// Represents a QUIC client on MsQuic, through .NET's <see cref="QuicConnection"/>, for Windows and Linux.
/// </summary>
/// <remarks>
/// Its connections use TLS 1.3 only, the "h3" ALPN token and no certificate revocation check, and abort streams and
/// close with error code 0 unless told otherwise.
/// </remarks>
public sealed class MsQuicClient : IQuicClient
{
    /// <summary>
    /// Gets a value indicating whether MsQuic connections are supported on this platform.
    /// </summary>
    public static bool IsSupported => QuicConnection.IsSupported;

    /// <inheritdoc/>
    public async ValueTask<IQuicConnection> ConnectAsync(
        QuicClientConnectOptions options, CancellationToken cancellationToken)
    {
        var clientOptions = new QuicClientConnectionOptions {
            RemoteEndPoint = options.RemoteEndPoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            MaxInboundBidirectionalStreams = options.MaxInboundBidirectionalStreams,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                RemoteCertificateValidationCallback = options.CertificateValidationCallback,
                EnabledSslProtocols = SslProtocols.Tls13,
                TargetHost = options.TargetHost,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption
            }
        };

        var connection = await QuicConnection.ConnectAsync(clientOptions, cancellationToken).Vhc();
        return new MsQuicConnection(connection);
    }
}
