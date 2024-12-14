using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.HttpTesters;

public class HttpTesterClient(IPEndPoint serverEp, string? domain, bool isHttps, TimeSpan? timeout = null)
    : IStreamTesterClient
{
    private string Scheme => isHttps ? "Https" : "Http";

    private Uri GetBaseUri()
    {
        var host = domain ?? serverEp.Address.ToString();
        var uriBuilder = new UriBuilder(Scheme.ToLower(), host, serverEp.Port);
        return uriBuilder.Uri;
    }

    public async Task Start(long upSize, long downSize, int connectionCount, CancellationToken cancellationToken)
    {
        if (upSize != 0)
            await StartUpload(upSize, connectionCount, cancellationToken);

        if (downSize != 0)
            await StartDownload(downSize, connectionCount, cancellationToken);
    }


    public async Task StartUpload(long size, int connectionCount, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation(
            $"{Scheme} => Start Uploading {VhUtil.FormatBytes(size)}, Connections: {connectionCount}");

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
        VhLogger.Instance.LogInformation(
            $"{Scheme} => Start Downloading {VhUtil.FormatBytes(size)}, Connections: {connectionCount}");

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
            using var httpClient = HttpClientUtil.CreateHttpClient(serverEp.Address, timeout);
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
            using var httpClient = HttpClientUtil.CreateHttpClient(serverEp.Address, timeout);
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
}