using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Test.QuicTesters;

public class QuicTesterClient(IPEndPoint serverEp, string domain, TimeSpan? timeout)
{
    public async Task<byte[]> SendAndReceive(byte[] data, CancellationToken cancellationToken)
    {
        // create quic connection options
        var options = new QuicClientConnectionOptions {
            RemoteEndPoint = serverEp,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                EnabledSslProtocols = SslProtocols.Tls13,
                TargetHost = domain,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption
            },
            HandshakeTimeout = timeout ?? TimeSpan.FromSeconds(5)
        };

        await using var connection = await QuicConnection.ConnectAsync(options, cancellationToken);
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);

        // send data
        await stream.WriteAsync(data, cancellationToken);
        stream.CompleteWrites();

        // receive echo
        using var memoryStream = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            memoryStream.Write(buffer, 0, bytesRead);

        VhLogger.Instance.LogInformation("QuicClient: Sent {SentBytes} bytes, received {ReceivedBytes} bytes.",
            data.Length, memoryStream.Length);

        return memoryStream.ToArray();
    }
}