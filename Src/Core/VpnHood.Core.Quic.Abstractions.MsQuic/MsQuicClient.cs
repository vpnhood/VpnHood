using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Quic.Abstractions.MsQuic;

public sealed class MsQuicClient : IQuicClient
{
    public static bool IsSupported => QuicConnection.IsSupported;

    public async ValueTask<IQuicConnection> ConnectAsync(
        QuicClientConnectOptions options, CancellationToken cancellationToken)
    {
        var clientOptions = new QuicClientConnectionOptions {
            RemoteEndPoint = options.RemoteEndPoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
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
