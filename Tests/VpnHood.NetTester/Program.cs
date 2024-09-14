using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.NetTester.CommandServers;

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
        var logger = VhLogger.CreateConsoleLogger();

        // stop the server
        if (args.First() == "stop") {
            // write a file called command
            await File.WriteAllTextAsync("stop_command", "stop");
            logger.LogInformation("Stop command has been send.");
            return;
        }

        // Server
        using var commandServer = args.Contains("/server") 
            ? CommandServer.Create(ArgumentUtils.Get<IPEndPoint>(args, "/ep")) 
            : null;

        // client
        if (args.Contains("/client")) {
            using var clientApp = await ClientApp.Create(new ClientOptions(args), logger);
            await clientApp.StartTest(CancellationToken.None);
        }


        //if (args.Any(x => x == "/self_udp")) {
        //    var udpEchoServer = new UdpEchoServer();
        //    _ = udpEchoServer.StartAsync();
        //    var udpEchoClient = new UdpEchoClient();
        //    await udpEchoClient.StartAsync(udpEchoServer.LocalEndPoint!, 1000, 1000);
        //}

        //if (args[0] == "client") {
        //    var dataLen = args.Length > 2 ? int.Parse(args[2]) : 1000;
        //    var echoCount = args.Length > 3 ? int.Parse(args[3]) : 1;
        //    var udpEchoClient = new UdpEchoClient();
        //    await udpEchoClient.StartAsync(serverEp, echoCount, dataLen);
        //}


        if (commandServer != null)
            await WaitForStop();
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