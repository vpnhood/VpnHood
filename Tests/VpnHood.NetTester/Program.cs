using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
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

        // start the command server
        var serverEp = GetArgument<IPEndPoint>(args, "/server", null) ?? throw new ArgumentException("Server endpoint is required. /server 1.1.1.1:31500");
        if (args.First() == "server") {
            using var commandServer = CommandServer.Create(serverEp);
            await WaitForStop();
            return;
        }

        // get parameters from command line /up
        var uploadBytes = GetArgument(args, "/up", 20) * 1000000; // 10MB
        var downloadBytes = GetArgument(args, "/down", 60) * 1000000; // 10MB
        var tcpPort = GetArgument(args, "/tcp", 33700); // 10MB
        var connectionCount = GetArgument(args, "/multi", 10); // 10MB

        // dump variables by logger
        logger.LogInformation("Upload: {Upload}, Download: {Download}, TcpPort: {TcpPort}, Multi: {connectionCount}", 
            VhUtil.FormatBytes(uploadBytes), VhUtil.FormatBytes(downloadBytes), tcpPort, connectionCount);

        if (args.Any(x => x == "/self_tcp")) {
            using var commandServer = CommandServer.Create(serverEp);
            using var clientApp = await ClientApp.Create(commandServer.EndPoint, new ServerConfig { TcpPort = tcpPort }, logger);
            await clientApp.FullTcpTest(uploadBytes: uploadBytes, downloadBytes: downloadBytes, connectionCount: connectionCount,
                CancellationToken.None);
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

        //if (args[0] == "server") {
        //    var udpEchoServer = new UdpEchoServer(serverEp);
        //    _ = udpEchoServer.StartAsync();
        //    await WaitForStop();
        //}
    }

    private static T? GetArgument<T>(string[] args, string name, T? defaultValue)
    {
        // find the index of the argument
        var index = Array.IndexOf(args, name);
        if (index == -1)
            return defaultValue;

        var value = args[index + 1];

        if (typeof(T) == typeof(string))
            return (T)(object)value;

        if (typeof(T) == typeof(int))
            return (T)(object)int.Parse(value);

        if (typeof(T) == typeof(IPAddress))
            return (T)(object)IPAddress.Parse(value);

        if (typeof(T) == typeof(IPEndPoint))
            return (T)(object)IPEndPoint.Parse(value);

        return defaultValue;
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