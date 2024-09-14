using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;
using VpnHood.NetTester.Streams;

namespace VpnHood.NetTester.HttpTesters;

public class HttpTesterClient
{
    public static async Task StartSingle(IPEndPoint serverEp, long uploadLength, long downloadLength,
        ILogger? logger, CancellationToken cancellationToken)
    {
        if (uploadLength != 0)
            await SingleUpload(serverEp, uploadLength, logger, cancellationToken);

        if (downloadLength != 0)
            await SingleDownload(serverEp, downloadLength, logger, cancellationToken);
    }

    public static async Task StartMulti(IPEndPoint serverEp, long uploadLength, long downloadLength, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        if (downloadLength != 0)
            await MultiUpload(serverEp, uploadLength, connectionCount, logger, cancellationToken);

        if (downloadLength != 0)
            await MultiDownload(serverEp, downloadLength, connectionCount, logger, cancellationToken);
    }


    public static async Task SingleUpload(IPEndPoint serverEp, long length, ILogger? logger, CancellationToken cancellationToken)
    {
        using var speedometer = new Speedometer("SingleHttp => Upload");
        logger?.LogInformation($"{DateTime.Now:T}: SingleTcp => Start uploading {VhUtil.FormatBytes(length)}");
        await StartUpload(serverEp, length, speedometer, cancellationToken);
    }

    public static async Task SingleDownload(IPEndPoint serverEp, long length, ILogger? logger, CancellationToken cancellationToken)
    {
        using var speedometer = new Speedometer("SingleHttp => Download");
        logger?.LogInformation($"{DateTime.Now:T}: SingleTcp => Start downloading {VhUtil.FormatBytes(length)}");
        await StartDownload(serverEp, length, speedometer, cancellationToken);
    }

    public static async Task MultiUpload(IPEndPoint serverEp, long length, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        logger?.LogInformation($"{DateTime.Now:T}: MultiHttp => Start uploading {VhUtil.FormatBytes(length)}, Multi: {connectionCount}x");

        // start multi uploaders
        using var speedometer1 = new Speedometer("MultiHttp => Upload");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++) {
            uploadTasks[i] = StartUpload(serverEp, length / connectionCount, speedometer: speedometer1,
                cancellationToken: cancellationToken);
        }
        await Task.WhenAll(uploadTasks);
    }

    public static async Task MultiDownload(IPEndPoint serverEp, long length, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        logger?.LogInformation($"{DateTime.Now:T}: MultiHttp => Start downloading {VhUtil.FormatBytes(length)}, Multi: {connectionCount}x");

        // start multi downloader
        using var speedometer1 = new Speedometer("MultiHttp => Download");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++) {
            uploadTasks[i] = StartDownload(serverEp, length / connectionCount, speedometer: speedometer1,
                cancellationToken: cancellationToken);
        }
        await Task.WhenAll(uploadTasks);
    }

    private static async Task StartUpload(IPEndPoint serverEp, long length,
        Speedometer speedometer, CancellationToken cancellationToken)
    {
        // Create a custom stream that generates random data on the fly
        await using var contentStream = new StreamRandomReader(length, speedometer);
        var content = new StreamContent(contentStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        // Upload the content to the server
        var httpClient = new HttpClient();
        var requestUri = $"http://{serverEp}/upload";
        var response = await httpClient.PostAsync(requestUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task StartDownload(IPEndPoint serverEp, long length, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        // Upload the content to the server
        var httpClient = new HttpClient();
        var requestUri = $"http://{serverEp}/download?length={length}";
        await using var stream = await httpClient.GetStreamAsync(requestUri, cancellationToken);

        // read all data from the stream
        await using var streamDiscarder = new StreamDiscarder(speedometer);
        await stream.CopyToAsync(streamDiscarder, cancellationToken);
    }
}

