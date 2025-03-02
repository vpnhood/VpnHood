using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.QuicTesters;

public class QuicTesterServer(
    IPEndPoint quicEndPoint,
    X509Certificate2 certificate,
    CancellationToken cancellationToken)
    : IDisposable
{
    private QuicListener? _quicListener;

    public IPEndPoint ListenerEndPoint =>
        _quicListener?.LocalEndPoint ?? throw new InvalidOperationException("Server has not been started.");

    public async Task Start()
    {
        if (_quicListener != null)
            throw new InvalidOperationException("Quic Server is already running.");

        VhLogger.Instance.LogInformation("QuicServer: Starting... EndPoint: {EndPoint}", quicEndPoint);

        // create quic listener options
        var listenerOptions = new QuicListenerOptions {
            ApplicationProtocols = [SslApplicationProtocol.Http3],
            ListenEndPoint = quicEndPoint,
            ConnectionOptionsCallback = ConnectionOptionsCallback
        };

        // create listener
        try {
            _quicListener = await QuicListener.ListenAsync(listenerOptions, cancellationToken);

            // start accepting connections
            while (!cancellationToken.IsCancellationRequested) {
                var client = await _quicListener.AcceptConnectionAsync(cancellationToken);
                _ = ProcessConnection(client, cancellationToken);
            }

            VhLogger.Instance.LogInformation("QuicServer: Started... EndPoint: {EndPoint}", ListenerEndPoint);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "QuicServer: Error.");
        }
        finally {
            VhLogger.Instance.LogInformation("QuicServer: Error.");
        }
    }

    private ValueTask<QuicServerConnectionOptions> ConnectionOptionsCallback(
        QuicConnection quicConnection, SslClientHelloInfo sslClientHelloInfo, CancellationToken ct)
    {
        var ret = new QuicServerConnectionOptions {
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            IdleTimeout = TimeSpan.FromMinutes(10),
            ServerAuthenticationOptions = new SslServerAuthenticationOptions {
                ServerCertificate = certificate,
                ServerCertificateSelectionCallback = (_, _) => certificate,
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        return new ValueTask<QuicServerConnectionOptions>(ret);
    }

    private static async Task ProcessConnection(QuicConnection quickConnection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Server: Start processing quickConnection. ClientEp: {ClientEp}",
            quickConnection.RemoteEndPoint);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var stream = await quickConnection.AcceptInboundStreamAsync(cancellationToken);
                _ = ProcessQuickStream(stream, cancellationToken);
            }
        }
        finally {
            await quickConnection.DisposeAsync();
        }
    }

    private static async Task ProcessQuickStream(QuicStream stream, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Server: Start processing a QUIC stream.");
        try {
            // find read size
            var buffer = new byte[16];
            await stream.ReadAtLeastAsync(buffer, 16, true, cancellationToken);

            var readSize = BitConverter.ToInt64(buffer, 0);
            var writeSize = BitConverter.ToInt64(buffer, 8);

            // read data
            await using var discarder = new StreamDiscarder(null);
            await discarder.ReadFromAsync(stream, size: readSize, cancellationToken: cancellationToken);

            // write data
            await using var randomReader = new StreamRandomReader(writeSize, null);
            await randomReader.CopyToAsync(stream, cancellationToken);
        }
        finally {
            VhLogger.Instance.LogInformation("Server: Finish processing a QUIC stream.");
            await stream.DisposeAsync();
        }
    }

    public void Dispose()
    {
        _quicListener?.DisposeAsync().AsTask().Wait();
    }
}