using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Test.QuicTesters;

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
        }
        catch (Exception ex) {
            if (!cancellationToken.IsCancellationRequested)
                VhLogger.Instance.LogInformation(ex, "QuicServer: Error.");
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

    private static async Task ProcessConnection(QuicConnection quicConnection, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("QuicServer: Start processing connection. ClientEp: {ClientEp}",
            quicConnection.RemoteEndPoint);
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var stream = await quicConnection.AcceptInboundStreamAsync(cancellationToken);
                _ = ProcessQuicStream(stream, cancellationToken);
            }
        }
        finally {
            await quicConnection.DisposeAsync();
        }
    }

    private static async Task ProcessQuicStream(QuicStream stream, CancellationToken cancellationToken)
    {
        try {
            // echo: read data and send it back
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);

            // signal to the client that no more data will be sent
            stream.CompleteWrites();
        }
        catch (Exception ex) {
            if (!cancellationToken.IsCancellationRequested)
                VhLogger.Instance.LogInformation(ex, "QuicServer: Error processing QUIC stream.");
        }
        finally {
            await stream.DisposeAsync();
        }
    }

    public void Dispose()
    {
        _quicListener?.DisposeAsync().AsTask().Wait();
    }
}