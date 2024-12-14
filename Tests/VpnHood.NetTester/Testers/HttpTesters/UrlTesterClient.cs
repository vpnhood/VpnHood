using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.HttpTesters;

public class UrlTesterClient(Uri url, IPAddress? serverIp, TimeSpan? timeout = null) 
    : IStreamTesterClient
{
    public async Task Start(long upSize, long downSize, int connectionCount, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation("Url => Start Downloading. Size: {Size}, Connections: {Connections}, Url: {Url}, serverIp: {serverIp}",
            VhUtil.FormatBytes(downSize), connectionCount, url, serverIp);

        // start multi downloader
        using var speedometer = new Speedometer("Down");
        var uploadTasks = new Task[connectionCount];
        for (var i = 0; i < connectionCount; i++)
            uploadTasks[i] = SimpleDownload(url, downSize / connectionCount, serverIp, speedometer: speedometer,
                timeout, cancellationToken: cancellationToken);

        await Task.WhenAll(uploadTasks);
    }

    private static async Task SimpleDownload(Uri url, long size, IPAddress? serverIp, Speedometer speedometer,
        TimeSpan? timeout, CancellationToken cancellationToken)
    {
        try {
            using var client = HttpClientUtil.CreateHttpClient(serverIp, timeout);
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