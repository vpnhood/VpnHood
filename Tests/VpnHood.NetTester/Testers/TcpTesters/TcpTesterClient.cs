using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.NetTester.Streams;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.TcpTesters;

public class TcpTesterClient
{
    public static async Task StartSingle(IPEndPoint serverEp, long upLength, long downLength,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"SingleTcp => Start Uploading {VhUtil.FormatBytes(upLength)}");
        TcpClient tcpClient;
        using (var speedometer = new Speedometer("SingleTcp => Up"))
            tcpClient = await StartUpload(serverEp, upLength: upLength, downLength: downLength,
                speedometer: speedometer, cancellationToken: cancellationToken);

        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"SingleTcp => Start Downloading {VhUtil.FormatBytes(downLength)}");
        using (var speedometer = new Speedometer("SingleTcp => Down"))
            await StartDownload(tcpClient.GetStream(), downLength, speedometer, cancellationToken);

        tcpClient.Dispose();
    }


    public static async Task StartMulti(IPEndPoint serverEp, long upLength, long downLength, int connectionCount,
        CancellationToken cancellationToken)
    {
        var uploadTasks = new Task<TcpClient>[connectionCount];

        // start multi uploaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation(
            $"MultiTcp => Start Uploading {VhUtil.FormatBytes(upLength)}, Multi: {connectionCount}x");
        using (var speedometer = new Speedometer("MultiTcp => Up")) {
            for (var i = 0; i < connectionCount; i++)
                uploadTasks[i] = StartUpload(serverEp, upLength: upLength, downLength: downLength,
                    speedometer: speedometer, cancellationToken: cancellationToken);

            await Task.WhenAll(uploadTasks);
        }

        // start multi downloaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation(
            $"MultiTcp => Start Downloading {VhUtil.FormatBytes(downLength)}, Multi: {connectionCount}x");
        using (var speedometer = new Speedometer("MultiTcp => Down")) {
            var downloadTasks = new Task<Stream>[connectionCount];
            for (var i = 0; i < connectionCount; i++)
                downloadTasks[i] = StartDownload(uploadTasks[i].Result.GetStream(), downLength, speedometer,
                    cancellationToken);

            await Task.WhenAll(downloadTasks);
        }

        // dispose streams
        foreach (var uploadTask in uploadTasks.Where(x => x.IsCompletedSuccessfully)) uploadTask.Result.Dispose();
    }

    private static async Task<TcpClient> StartUpload(IPEndPoint serverEp, long upLength, long downLength,
        Speedometer speedometer, CancellationToken cancellationToken)
    {
        // connect to server
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(serverEp.Address, serverEp.Port, cancellationToken);

        var stream = tcpClient.GetStream();

        // write upLength to server
        var buffer = BitConverter.GetBytes(upLength);
        await stream.WriteAsync(buffer, 0, 8, cancellationToken);

        // write downLength to server
        buffer = BitConverter.GetBytes(downLength);
        await stream.WriteAsync(buffer, 0, 8, cancellationToken);

        // write random data
        await using var random = new StreamRandomReader(upLength, speedometer);
        await random.CopyToAsync(stream, cancellationToken);
        return tcpClient;
    }

    private static async Task<Stream> StartDownload(Stream stream, long length, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        // read from server
        await using var discarder = new StreamDiscarder(speedometer);
        await discarder.ReadFromAsync(stream, length: length, cancellationToken: cancellationToken);
        return stream;
    }
}