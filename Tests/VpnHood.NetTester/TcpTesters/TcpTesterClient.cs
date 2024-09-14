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
        await StartSingle(serverEp, uploadBytes, downloadBytes, connectionCount, logger, cancellationToken);
        await StartMulti(serverEp, uploadBytes, downloadBytes, connectionCount, logger, cancellationToken);
    }

    public static async Task StartSingle(IPEndPoint serverEp, long uploadBytes, long downloadBytes, int connectionCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        // 
        using var speedometer1 = new Speedometer("SingleTcp => Upload");
        Console.WriteLine($"{DateTime.Now:T}: SingleTcp => Start uploading {VhUtil.FormatBytes(uploadBytes)}");
        await using var stream = await StartUpload(serverEp, bytes: uploadBytes,
            speedometer: speedometer1, cancellationToken: cancellationToken);
        speedometer1.Stop();

        using var speedometer2 = new Speedometer("SingleTcp => Download");
        Console.WriteLine($"{DateTime.Now:T}: SingleTcp => Start downloading {VhUtil.FormatBytes(downloadBytes)}");
        await StartDownload(stream, downloadBytes, speedometer2, cancellationToken);
        speedometer2.Stop();
    }


    public static async Task StartMulti(IPEndPoint serverEp, long uploadBytes, long downloadBytes, int connectionCount, ILogger? logger,
        CancellationToken cancellationToken)
    {
        var uploadTasks = new Task<Stream>[connectionCount];

        // start multi uploaders
        using var speedometer1 = new Speedometer("MultiTcp => Upload");
        Console.WriteLine($"{DateTime.Now:T}: MultiTcp => Start uploading {VhUtil.FormatBytes(uploadBytes)}, Multi: {connectionCount}x");
        for (var i = 0; i < connectionCount; i++) {
            uploadTasks[i] = StartUpload(serverEp, uploadBytes, speedometer: speedometer1, cancellationToken: cancellationToken);
        }
        await Task.WhenAll(uploadTasks);
        speedometer1.Stop();

        // start multi downloaders
        using var speedometer2 = new Speedometer("MultiTcp => Download");
        Console.WriteLine($"{DateTime.Now:T}: MultiTcp => Start downloading {VhUtil.FormatBytes(downloadBytes)}, Multi: {connectionCount}x");
        var downloadTasks = new Task<Stream>[connectionCount];
        for (var i = 0; i < connectionCount; i++) {
            downloadTasks[i] = StartDownload(uploadTasks[i].Result, downloadBytes, speedometer2, cancellationToken);
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