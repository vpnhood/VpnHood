using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Net.Quic.Abstractions;
using SystemQuicListenerOptions = System.Net.Quic.QuicListenerOptions;
// disambiguate from System.Net.Quic.QuicListenerOptions, which `using System.Net.Quic` also imports
using QuicListenerOptions = VpnHood.Net.Quic.Abstractions.QuicListenerOptions;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Quic.MsQuic;

/// <summary>
/// Represents a QUIC server on MsQuic, through .NET's <see cref="QuicListener"/>, for Windows and Linux.
/// </summary>
/// <remarks>
/// Its listeners use TLS 1.3 only and the "h3" ALPN token, ask for no client certificate and check no revocation.
/// </remarks>
public sealed class MsQuicServer : IQuicServer
{
    /// <summary>
    /// Gets a value indicating whether MsQuic listeners are supported on this platform.
    /// </summary>
    public static bool IsSupported => QuicListener.IsSupported;

    /// <inheritdoc/>
    public async ValueTask<IQuicListener> ListenAsync(
        QuicListenerOptions options, CancellationToken cancellationToken)
    {
        var listenerOptions = new SystemQuicListenerOptions {
            ListenEndPoint = options.ListenEndPoint,
            ApplicationProtocols = [SslApplicationProtocol.Http3],
            ConnectionOptionsCallback = (_, _, _) => ConnectionOptionsCallback(options)
        };

        var listener = await QuicListener.ListenAsync(listenerOptions, cancellationToken).Vhc();
        return new MsQuicListener(listener);
    }

    private static ValueTask<QuicServerConnectionOptions> ConnectionOptionsCallback(QuicListenerOptions options)
    {
        var serverOptions = new QuicServerConnectionOptions {
            DefaultStreamErrorCode = options.DefaultStreamErrorCode,
            DefaultCloseErrorCode = options.DefaultCloseErrorCode,
            MaxInboundBidirectionalStreams = options.MaxInboundBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxInboundUnidirectionalStreams,
            IdleTimeout = options.IdleTimeout,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions {
                ServerCertificateSelectionCallback = (_, hostName) => options.ServerCertificateSelector(hostName),
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        return new ValueTask<QuicServerConnectionOptions>(serverOptions);
    }
}
