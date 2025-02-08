using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.QuicTesters;

public class QuicTesterClient(IPEndPoint serverEp, string domain, TimeSpan? timeout)
    : IStreamTesterClient
{
    public async Task Start(long upSize, long downSize, int connectionCount,
        CancellationToken cancellationToken)
    {
        var uploadTasks = new List<Task<ConnectionStream?>>();

        // start multi uploaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation(
            $"Quic => Start Uploading {VhUtil.FormatBytes(upSize)}, Connections: {connectionCount}");
        using (var speedometer = new Speedometer("Up")) {
            for (var i = 0; i < connectionCount; i++)
                uploadTasks.Add(StartUpload(upSize: upSize / connectionCount, downSize: downSize / connectionCount,
                    speedometer: speedometer, cancellationToken: cancellationToken));

            await Task.WhenAll(uploadTasks);
        }

        // start multi downloaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation(
            $"QUIC => Start Downloading {VhUtil.FormatBytes(downSize)}, Connections: {connectionCount}");
        using (var speedometer = new Speedometer("Down")) {
            var downloadTasks = uploadTasks
                .Where(x => x is { IsCompletedSuccessfully: true, Result: not null })
                .Select(x =>
                    StartDownload(x.Result!.Stream, downSize / connectionCount, speedometer, cancellationToken))
                .ToArray();

            await Task.WhenAll(downloadTasks);
        }

        // dispose streams
        foreach (var uploadTask in uploadTasks.Where(x => x.IsCompletedSuccessfully)) {
            var connectionStream = await uploadTask;
            if (connectionStream != null)
                await connectionStream.DisposeAsync();
        }
    }

    private async Task<ConnectionStream?> StartUpload(long upSize, long downSize,
        Speedometer speedometer, CancellationToken cancellationToken)
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
            IdleTimeout = timeout ?? TimeSpan.FromSeconds(15)
        };

        QuicConnection? connection = null;
        Stream? stream = null;
        try {
            connection = await QuicConnection.ConnectAsync(options, cancellationToken);
            stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);

            // write upSize to server
            var buffer = BitConverter.GetBytes(upSize);
            await stream.WriteAsync(buffer, 0, 8, cancellationToken);

            // write downSize to server
            buffer = BitConverter.GetBytes(downSize);
            await stream.WriteAsync(buffer, 0, 8, cancellationToken);

            // write random data
            await using var random = new StreamRandomReader(upSize, speedometer);
            await random.CopyToAsync(stream, cancellationToken);

            return new ConnectionStream {
                QuicConnection = connection,
                Stream = stream
            };
        }
        catch (Exception ex) {
            connection?.DisposeAsync();
            stream?.DisposeAsync();
            VhLogger.Instance.LogInformation(ex, "Error in QUIC transfer.");
            return null;
        }
    }

    private static async Task StartDownload(Stream stream, long size, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        try {
            // read from server
            await using var discarder = new StreamDiscarder(speedometer);
            await discarder.ReadFromAsync(stream, size: size, cancellationToken: cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Error in download via QUIC.");
        }
    }

    private class ConnectionStream : IAsyncDisposable
    {
        public required QuicConnection QuicConnection { get; init; }
        public required Stream Stream { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            await QuicConnection.DisposeAsync();
        }
    }
}