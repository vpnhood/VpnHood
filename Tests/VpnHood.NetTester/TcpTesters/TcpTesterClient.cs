using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.NetTester.TcpTesters;

public class TcpTesterClient
{
    public static async Task StartFull(IPEndPoint serverEp, long writeBytes, long readBytes, int concurrentCount,
        ILogger? logger, CancellationToken cancellationToken)
    {
        // start simple tester
        using (var speedometer = new Speedometer("SingleTcp")) {
            await Start(serverEp, writeBytes: writeBytes, readBytes: readBytes,
                speedometer: speedometer, logger: logger, cancellationToken: cancellationToken);
        }

        // start concurrent testers
        using (var speedometer = new Speedometer("ConcurrentTcp")) {
            await StartConcurrent(serverEp, writeBytes: writeBytes / concurrentCount,
                readBytes: readBytes / concurrentCount,
                concurrentCount: concurrentCount,
                speedometer: speedometer, logger: logger, cancellationToken: cancellationToken);
        }
    }

    public static async Task StartConcurrent(IPEndPoint serverEp, long writeBytes, long readBytes, int concurrentCount,
        Speedometer speedometer, ILogger? logger, CancellationToken cancellationToken)
    {
        logger?.LogInformation("Concurrent TCP tester has been started. Write: {Write}, Read:{Read}",
            VhUtil.FormatBytes(writeBytes), VhUtil.FormatBytes(readBytes));

        var tasks = new Task[concurrentCount];
        for (var i = 0; i < concurrentCount; i++)
            tasks[i] = Start(serverEp, writeBytes, readBytes, speedometer: speedometer, logger: null, cancellationToken: cancellationToken);

        await Task.WhenAll(tasks);

        logger?.LogInformation("Concurrent TCP tester has been completed.");
    }

    public static async Task Start(IPEndPoint serverEp, long writeBytes, long readBytes,
        Speedometer speedometer, ILogger? logger, CancellationToken cancellationToken)
    {
        logger?.LogInformation("Single TCP tester has been started. Write: {Write}, Read:{Read}",
            VhUtil.FormatBytes(writeBytes), VhUtil.FormatBytes(readBytes));

        // connect to server
        var client = new TcpClient();
        await client.ConnectAsync(serverEp.Address, serverEp.Port, cancellationToken);

        // write writeBytes to server
        await using var stream = client.GetStream();
        var buffer = BitConverter.GetBytes(writeBytes);
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        await TcpTesterUtil.WriteRandomData(stream, writeBytes, speedometer: speedometer, cancellationToken: cancellationToken);
        await TcpTesterUtil.ReadData(stream, readBytes, speedometer: speedometer, cancellationToken: cancellationToken);

        logger?.LogInformation("Single TCP tester has been completed.");
    }

}