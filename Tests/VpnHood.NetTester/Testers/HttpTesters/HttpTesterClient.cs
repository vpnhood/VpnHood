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

public class HttpTesterClient(IPEndPoint serverEp, string? domain, bool isHttps)
{
    private string Scheme => isHttps ? "Https" : "Http";
    private Uri GetBaseUri()
    {
        var host = domain ?? serverEp.Address.ToString();
        var uriBuilder = new UriBuilder(Scheme.ToLower(), host, serverEp.Port);
        return uriBuilder.Uri;
    }

    private IPAddress ResolveDns(string value)
    {
        // check if it is an IP address
        if (IPAddress.TryParse(value, out var ipAddress))
            return ipAddress;

        // check if it is our domain
        if (value.Equals(domain, StringComparison.OrdinalIgnoreCase))
            return serverEp.Address;

        // resolve the domain
        var ipAddresses = Dns.GetHostAddresses(value);
        if (ipAddresses.Length == 0)
            throw new InvalidOperationException($"Cannot resolve domain: {value}");

        return ipAddresses[0];
    }

    public async Task Start(long upSize, long downSize, int connectionCount,
        CancellationToken cancellationToken)
    {
        if (downSize != 0)
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
            using var httpClient = CreateHttpClient();
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
            using var httpClient = CreateHttpClient();
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
    private HttpClient CreateHttpClient()
    {
        var socketHttpHandler = new SocketsHttpHandler {
            ConnectCallback = async (context, cancellationToken) => {
                var ipAddress = ResolveDns(context.DnsEndPoint.Host);
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

        //var handler = new HttpClientHandler();
        //handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        //handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        //var client = new HttpClient(handler);
        var client = new HttpClient(socketHttpHandler);
        return client;
    }

    public static async Task SimpleDownload(Uri url, int size, int connectionCount, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"Url => Start Downloading {VhUtil.FormatBytes(size)}, Connections: {connectionCount}, Url: {url}");

        // start multi downloader
        using var speedometer = new Speedometer("Down");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++)
            uploadTasks[i] = SimpleDownload(url, size / connectionCount, speedometer: speedometer,
                cancellationToken: cancellationToken);

        await Task.WhenAll(uploadTasks);
    }

    private static async Task SimpleDownload(Uri url, int size, Speedometer speedometer, CancellationToken cancellationToken)
    {
        try {
            using var client = new HttpClient();
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

