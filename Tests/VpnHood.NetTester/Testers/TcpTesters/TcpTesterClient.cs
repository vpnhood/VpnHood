using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.TcpTesters;

public class TcpTesterClient(IPEndPoint serverEp) : IStreamTesterClient
{
    public async Task Start(long upSize, long downSize, int connectionCount, CancellationToken cancellationToken)
    {
        var uploadTasks = new Task<TcpClient>[connectionCount];

        // start multi uploaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"Tcp => Start Uploading {VhUtil.FormatBytes(upSize)}, Connections: {connectionCount}");
        using (var speedometer = new Speedometer("Up")) {
            for (var i = 0; i < connectionCount; i++)
                uploadTasks[i] = StartUpload(serverEp, upSize: upSize / connectionCount, downSize: downSize / connectionCount,
                    speedometer: speedometer, cancellationToken: cancellationToken);

            await Task.WhenAll(uploadTasks);
        }

        // start multi downloaders
        VhLogger.Instance.LogInformation("\n--------");
        VhLogger.Instance.LogInformation($"Tcp => Start Downloading {VhUtil.FormatBytes(downSize)}, Connections: {connectionCount}");
        using (var speedometer = new Speedometer("Down")) {
            var downloadTasks = new Task[connectionCount];
            for (var i = 0; i < connectionCount; i++)
                if ((await uploadTasks[i]).Connected)
                    downloadTasks[i] = StartDownload((await uploadTasks[i]).GetStream(), downSize / connectionCount, speedometer,
                    cancellationToken);

            await Task.WhenAll(downloadTasks);
        }

        // dispose streams
        foreach (var uploadTask in uploadTasks.Where(x => x.IsCompletedSuccessfully)) 
            (await uploadTask).Dispose();
    }

    private static async Task<TcpClient> StartUpload(IPEndPoint serverEp, long upSize, long downSize,
        Speedometer speedometer, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        try {
            // connect to server
            await tcpClient.ConnectAsync(serverEp.Address, serverEp.Port, cancellationToken);

            var stream = tcpClient.GetStream();

            // write upSize to server
            var buffer = BitConverter.GetBytes(upSize);
            await stream.WriteAsync(buffer, 0, 8, cancellationToken);

            // write downSize to server
            buffer = BitConverter.GetBytes(downSize);
            await stream.WriteAsync(buffer, 0, 8, cancellationToken);

            // write random data
            await using var random = new StreamRandomReader(upSize, speedometer);
            await random.CopyToAsync(stream, cancellationToken);
            return tcpClient;

        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Error in upload via TCP.");
            return tcpClient;
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
            VhLogger.Instance.LogInformation(ex, "Error in download via TCP.");
        }
    }
}