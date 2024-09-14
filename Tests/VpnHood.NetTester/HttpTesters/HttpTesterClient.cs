using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;
using VpnHood.NetTester.Streams;

namespace VpnHood.NetTester.HttpTesters;

public class HttpTesterClient
{
    public static async Task StartSingle(IPEndPoint serverEp, long upLength, long downLength,
        ILogger? logger, CancellationToken cancellationToken)
    {
        if (upLength != 0)
            await SingleUpload(serverEp, upLength, logger, cancellationToken);

        if (downLength != 0)
            await SingleDownload(serverEp, downLength, logger, cancellationToken);
    }

    public static async Task StartMulti(IPEndPoint serverEp, long upLength, long downLength, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        if (downLength != 0)
            await MultiUpload(serverEp, upLength, connectionCount, logger, cancellationToken);

        if (downLength != 0)
            await MultiDownload(serverEp, downLength, connectionCount, logger, cancellationToken);
    }


    public static async Task SingleUpload(IPEndPoint serverEp, long length, ILogger? logger, CancellationToken cancellationToken)
    {
        using var speedometer = new Speedometer("SingleHttp => Up");
        logger?.LogInformation($"{DateTime.Now:T}: SingleTcp => Start Uploading {VhUtil.FormatBytes(length)}");
        await StartUpload(serverEp, length, speedometer, cancellationToken);
    }

    public static async Task SingleDownload(IPEndPoint serverEp, long length, ILogger? logger, CancellationToken cancellationToken)
    {
        using var speedometer = new Speedometer("SingleHttp => Down");
        logger?.LogInformation($"{DateTime.Now:T}: SingleTcp => Start Downloading {VhUtil.FormatBytes(length)}");
        await StartDownload(serverEp, length, speedometer, cancellationToken);
    }

    public static async Task MultiUpload(IPEndPoint serverEp, long length, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        logger?.LogInformation($"{DateTime.Now:T}: MultiHttp => Start Uploading {VhUtil.FormatBytes(length)}, Multi: {connectionCount}x");

        // start multi uploaders
        using var speedometer1 = new Speedometer("MultiHttp => Up");
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
        logger?.LogInformation($"{DateTime.Now:T}: MultiHttp => Start Downloading {VhUtil.FormatBytes(length)}, Multi: {connectionCount}x");

        // start multi downloader
        using var speedometer1 = new Speedometer("MultiHttp => Down");
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

