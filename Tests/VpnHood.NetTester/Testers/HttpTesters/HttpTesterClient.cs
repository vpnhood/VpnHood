using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.NetTester.Streams;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.HttpTesters;

public class HttpTesterClient(IPEndPoint serverEp, string? domain, bool isHttps, TimeSpan? timeout = null)
{
    private string Scheme => isHttps ? "Https" : "Http";
    private Uri GetBaseUri()
    {
        var host = domain ?? serverEp.Address.ToString();
        var uriBuilder = new UriBuilder(Scheme.ToLower(), host, serverEp.Port);
        return uriBuilder.Uri;
    }

    public async Task Start(long upSize, long downSize, int connectionCount,
        CancellationToken cancellationToken)
    {
        if (upSize != 0)
            await StartUpload(upSize, connectionCount, cancellationToken);

        if (downSize != 0)
            await StartDownload(downSize, connectionCount, cancellationToken);
    }


    public async Task StartUpload(long size, int connectionCount, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"{Scheme} => Start Uploading {VhUtil.FormatBytes(size)}, Connections: {connectionCount}");

        // start multi uploaders
        using var speedometer = new Speedometer("Up");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++)
            uploadTasks[i] = StartUploadInternal(size / connectionCount, speedometer: speedometer,
                cancellationToken: cancellationToken);
        await Task.WhenAll(uploadTasks);
    }

    public async Task StartDownload(long size, int connectionCount,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"{Scheme} => Start Downloading {VhUtil.FormatBytes(size)}, Connections: {connectionCount}");

        // start multi downloader
        using var speedometer = new Speedometer("Down");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++)
            uploadTasks[i] = StartDownloadInternal(size / connectionCount, speedometer: speedometer,
                cancellationToken: cancellationToken);
        await Task.WhenAll(uploadTasks);
    }

    private async Task StartUploadInternal(long size, Speedometer speedometer, CancellationToken cancellationToken)
    {
        try {
            // Create a custom stream that generates random data on the fly
            await using var contentStream = new StreamRandomReader(size, speedometer);
            var content = new StreamContent(contentStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            // Upload the content to the server
            using var httpClient = CreateHttpClient(serverEp.Address, timeout);
            var requestUri = new Uri(GetBaseUri(), "upload");
            var response = await httpClient.PostAsync(requestUri, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Error in upload via HTTP.");
        }
    }

    private async Task StartDownloadInternal(long size, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        try {
            // Upload the content to the server
            using var httpClient = CreateHttpClient(serverEp.Address, timeout);
            httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);

            var requestUri = new Uri(GetBaseUri(), $"downloads?size={size}&file={Guid.NewGuid()}.pak");
            await using var stream = await httpClient.GetStreamAsync(requestUri, cancellationToken);

            // read all data from the stream
            await using var streamDiscarder = new StreamDiscarder(speedometer);
            await stream.CopyToAsync(streamDiscarder, cancellationToken);

        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Error in download via HTTP.");
        }
    }

    // Create a custom HttpClient with custom SocketsHttpHandler
    // to resolve the domain and accept all certificates
    // for this example we are not validating the certificate
    private static HttpClient CreateHttpClient(IPAddress? ipAddress, TimeSpan? timeout)
    {
        if (ipAddress == null) {
            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            httpClientHandler.ClientCertificateOptions = ClientCertificateOption.Manual;
            return new HttpClient(httpClientHandler) {
                Timeout = timeout ?? TimeSpan.FromSeconds(15)
            };
        }

        var socketHttpHandler = new SocketsHttpHandler {
            ConnectCallback = async (context, cancellationToken) => {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) {
                    NoDelay = true
                };

                try {
                    await socket.ConnectAsync(ipAddress, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            },
            SslOptions = new SslClientAuthenticationOptions {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                RemoteCertificateValidationCallback = (_, _, _, _) =>
                    true
            }
        };

        var client = new HttpClient(socketHttpHandler) {
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };
        return client;

    }



    public static async Task SimpleDownload(Uri url, int size, IPAddress? ipAddress, int connectionCount, 
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation("Url => Start Downloading. Size: {Size}, Connections: {Connections}, Url: {Url}, Ip: {Ip}",
            VhUtil.FormatBytes(size), connectionCount, url, ipAddress);

        // start multi downloader
        using var speedometer = new Speedometer("Down");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++)
            uploadTasks[i] = SimpleDownload(url, size / connectionCount, ipAddress, speedometer: speedometer,
                timeout, cancellationToken: cancellationToken);

        await Task.WhenAll(uploadTasks);
    }

    private static async Task SimpleDownload(Uri url, int size, IPAddress? ipAddress, Speedometer speedometer, 
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        try {
            using var client = CreateHttpClient(ipAddress, timeout);
            var stream = await client.GetStreamAsync(url, cancellationToken);

            // read size data to discarder
            await using var streamDiscarder = new StreamDiscarder(speedometer);
            await streamDiscarder.ReadFromAsync(stream, size, cancellationToken: cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Error downloading a url.");
        }
    }
}

