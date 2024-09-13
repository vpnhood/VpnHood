using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Utils;
using VpnHood.NetTester.TcpTesters;

namespace VpnHood.NetTester;

internal class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0 || args.Any(x => x is "/?" or "-?" or "--help")) {
            Console.WriteLine("Usage:");
            Console.WriteLine("udptester client serverEndPoint dataLength echoCount");
            Console.WriteLine("udptester server <endpoint>");
            return;
        }

        // Create a logger
        using var loggerFactory = LoggerFactory.Create(builder => {
            builder.AddConsole();
        });
        var logger = loggerFactory.CreateLogger<TcpTesterClient>();


        if (args.Any(x=>x == "/stop")) {
            // write a file called command
            await File.WriteAllTextAsync("stop_command", "stop");
            Console.WriteLine("Stop command has been send.");
            return;
        }

        if (args.Any(x => x == "/self_tcp")) {
            // create server
            var cancellationTokenSource = new CancellationTokenSource();
            using var tcpTesterServer = new TcpTesterServer();
            _ = tcpTesterServer.Start(VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback), cancellationTokenSource.Token);

            // run client
            await Task.Delay(1000, cancellationTokenSource.Token);
            await TcpTesterClient.StartFull(tcpTesterServer.ListenerEndPoint, 10_000_000_000, 50_000_000_000, 
                concurrentCount: 10,
                logger: logger, cancellationToken: cancellationTokenSource.Token);
        }


        if (args.Any(x => x == "self_udp")) {
            var udpEchoServer = new UdpEchoServer();
            _ = udpEchoServer.StartAsync();
            var udpEchoClient = new UdpEchoClient();
            await udpEchoClient.StartAsync(udpEchoServer.LocalEndPoint!, 1000, 1000);
        }

        if (args[0] == "client") {
            var serverEp = IPEndPoint.Parse(args[1]);
            var dataLen = args.Length > 2 ? int.Parse(args[2]) : 1000;
            var echoCount = args.Length > 3 ? int.Parse(args[3]) : 1;
            var udpEchoClient = new UdpEchoClient();
            await udpEchoClient.StartAsync(serverEp, echoCount, dataLen);
        }

        if (args[0] == "server") {
            var serverEp = args.Length > 1 ? IPEndPoint.Parse(args[1]) : null;
            var udpEchoServer = new UdpEchoServer(serverEp);
            _ = udpEchoServer.StartAsync();
            await WaitForStop();
            return;
        }

        Console.WriteLine("first parameter can be client or server.");
    }

    private static async Task WaitForStop()
    {
        while (true) {
            if (File.Exists("stop_command"))
                break;

            await Task.Delay(1000);
        }
    }

}