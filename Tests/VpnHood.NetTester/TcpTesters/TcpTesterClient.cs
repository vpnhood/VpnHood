using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.NetTester.TcpTesters;

public class TcpTesterClient
{
    public static async Task StartFull(IPEndPoint serverEp, long uploadBytes, long downloadBytes, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        await StartSingle(serverEp, uploadBytes, downloadBytes, logger, cancellationToken);
        await StartMulti(serverEp, uploadBytes, downloadBytes, connectionCount, logger, cancellationToken);
    }

    public static async Task StartSingle(IPEndPoint serverEp, long upLength, long downLength,
        ILogger? logger, CancellationToken cancellationToken)
    {
        
        using var speedometer1 = new Speedometer("SingleTcp => Up");
        Console.WriteLine($"{DateTime.Now:T}: SingleTcp => Start Uploading {VhUtil.FormatBytes(upLength)}");
        await using var stream = await StartUpload(serverEp, bytes: upLength,
            speedometer: speedometer1, cancellationToken: cancellationToken);
        speedometer1.Stop();

        using var speedometer2 = new Speedometer("SingleTcp => Down");
        Console.WriteLine($"{DateTime.Now:T}: SingleTcp => Start Downloading {VhUtil.FormatBytes(downLength)}");
        await StartDownload(stream, downLength, speedometer2, cancellationToken);
        speedometer2.Stop();
    }


    public static async Task StartMulti(IPEndPoint serverEp, long upLength, long downLength, int connectionCount, ILogger? logger,
        CancellationToken cancellationToken)
    {
        var uploadTasks = new Task<Stream>[connectionCount];

        // start multi uploaders
        using var speedometer1 = new Speedometer("MultiTcp => Up");
        Console.WriteLine($"{DateTime.Now:T}: MultiTcp => Start Uploading {VhUtil.FormatBytes(upLength)}, Multi: {connectionCount}x");
        for (var i = 0; i < connectionCount; i++) {
            uploadTasks[i] = StartUpload(serverEp, upLength, speedometer: speedometer1, cancellationToken: cancellationToken);
        }
        await Task.WhenAll(uploadTasks);
        speedometer1.Stop();

        // start multi downloaders
        using var speedometer2 = new Speedometer("MultiTcp => Down");
        Console.WriteLine($"{DateTime.Now:T}: MultiTcp => Start Downloading {VhUtil.FormatBytes(downLength)}, Multi: {connectionCount}x");
        var downloadTasks = new Task<Stream>[connectionCount];
        for (var i = 0; i < connectionCount; i++) {
            downloadTasks[i] = StartDownload(uploadTasks[i].Result, downLength, speedometer2, cancellationToken);
        }
        await Task.WhenAll(uploadTasks);
        speedometer2.Stop();
    }

    private static async Task<Stream> StartUpload(IPEndPoint serverEp, long bytes,
        Speedometer speedometer, CancellationToken cancellationToken)
    {
        // connect to server
        var client = new TcpClient();
        await client.ConnectAsync(serverEp.Address, serverEp.Port, cancellationToken);

        // write uploadBytes to server
        var stream = client.GetStream();
        var buffer = BitConverter.GetBytes(bytes);
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        await TcpTesterUtil.WriteRandomData(stream, bytes, speedometer: speedometer, cancellationToken: cancellationToken);
        return stream;
    }

    private static async Task<Stream> StartDownload(Stream stream, long bytes, Speedometer speedometer, CancellationToken cancellationToken)
    {
        await TcpTesterUtil.ReadData(stream, bytes, speedometer: speedometer, cancellationToken: cancellationToken);
        return stream;
    }


}