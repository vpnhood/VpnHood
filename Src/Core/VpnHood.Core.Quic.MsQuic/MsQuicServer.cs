using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Utils;
using SystemQuicListenerOptions = System.Net.Quic.QuicListenerOptions;
// disambiguate from System.Net.Quic.QuicListenerOptions, which `using System.Net.Quic` also imports
using QuicListenerOptions = VpnHood.Core.Quic.Abstractions.QuicListenerOptions;

namespace VpnHood.Core.Quic.MsQuic;

public sealed class MsQuicServer : IQuicServer
{
    public static bool IsSupported => QuicListener.IsSupported;

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
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            IdleTimeout = options.IdleTimeout,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions {
                ServerCertificateSelectionCallback = (_, hostName) => options.ServerCertificateSelector(hostName),
                ApplicationProtocols = [SslApplicationProtocol.Http3], // just to look normal, we use HTTP 1.1 actually
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        return new ValueTask<QuicServerConnectionOptions>(serverOptions);
    }
}
