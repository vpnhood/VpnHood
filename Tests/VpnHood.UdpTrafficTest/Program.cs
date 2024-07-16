using System.Net;

namespace VpnHood.UdpTrafficTest;

internal class Program
{
    public static Task Main(string[] args)
    {
        if (args.Length == 0 || args.Any(x => x is "/?" or "-?" or "--help")) {
            Console.WriteLine("Usage:");
            Console.WriteLine("udptester client serverEndPoint dataLength echoCount");
            Console.WriteLine("udptester server <endpoint>");
            return Task.CompletedTask;
        }

        if (args.Any(x => x == "self")) {
            var udpEchoServer = new UdpEchoServer();
            _ = udpEchoServer.StartAsync();
            var udpEchoClient = new UdpEchoClient();
            return udpEchoClient.StartAsync(udpEchoServer.LocalEndPoint!, 1000, 1000);
        }

        if (args[0] == "client") {
            var serverEp = IPEndPoint.Parse(args[1]);
            var dataLen = args.Length > 2 ? int.Parse(args[2]) : 1000;
            var echoCount = args.Length > 3 ? int.Parse(args[3]) : 1;
            var udpEchoClient = new UdpEchoClient();
            return udpEchoClient.StartAsync(serverEp, echoCount, dataLen);
        }

        if (args[0] == "server") {
            var serverEp = args.Length > 1 ? IPEndPoint.Parse(args[1]) : null;
            var udpEchoServer = new UdpEchoServer(serverEp);
            return udpEchoServer.StartAsync();
        }

        Console.WriteLine("first parameter can be client or server.");

        return Task.CompletedTask;
    }
}