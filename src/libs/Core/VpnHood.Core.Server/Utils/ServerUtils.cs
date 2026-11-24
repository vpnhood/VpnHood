using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Server.Utils;

internal static class ServerUtils
{
    // the pool's own minimum, as the process found it: the default is a multiple of it, so each configuration sets the
    // same, not a multiple of the last one's
    private static readonly int InitialMinCompletionPortThreads = GetMinCompletionPortThreads();

    public static int GetFreeUdpPort(AddressFamily addressFamily, int? start)
    {
        start ??= new Random().Next(1024, 9000);
        for (var port = start.Value; port < start + 1000; port++) {
            try {
                using var udpClient = new UdpClient(port, addressFamily);
                var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                                    throw new InvalidOperationException("LocalEndPoint is null");
                return localEndPoint.Port;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.AddressAlreadyInUse) {
                break;
            }
            catch {
                // try the next port but more
                if (start < 1024) {
                    start = 1024;
                    port = 1023; // will be incremented to 1024 in the next loop
                }
            }
        }

        // try any port
        try {
            using var udpClient = new UdpClient(0, addressFamily);
            var localEndPoint = (IPEndPoint?)udpClient.Client.LocalEndPoint ??
                                throw new InvalidOperationException("LocalEndPoint is null");
            return localEndPoint.Port;
        }
        catch {
            return 0;
        }
    }

    public static void ConfigMinIoThreads(int? minCompletionPortThreads)
    {
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var newMinCompletionPortThreads);
        minCompletionPortThreads ??= InitialMinCompletionPortThreads * 30;
        if (minCompletionPortThreads != 0) newMinCompletionPortThreads = minCompletionPortThreads.Value;
        if (!ThreadPool.SetMinThreads(minWorkerThreads, newMinCompletionPortThreads)) {
            VhLogger.Instance.LogWarning(
                "The thread pool refused its least I/O completion threads: {MinCompletionPortThreads}",
                newMinCompletionPortThreads);

            return;
        }

        VhLogger.Instance.LogInformation(
            "MinWorkerThreads: {MinWorkerThreads}, MinCompletionPortThreads: {newMinCompletionPortThreads}",
            minWorkerThreads, newMinCompletionPortThreads);
    }

    public static void ConfigMaxIoThreads(int? maxCompletionPortThreads)
    {
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var newMaxCompletionPortThreads);
        maxCompletionPortThreads ??= 0xFFFF; // We prefer all IO operations get slow together than be queued
        if (maxCompletionPortThreads != 0) newMaxCompletionPortThreads = maxCompletionPortThreads.Value;
        if (!ThreadPool.SetMaxThreads(maxWorkerThreads, newMaxCompletionPortThreads)) {
            VhLogger.Instance.LogWarning(
                "The thread pool refused its most I/O completion threads: {MaxCompletionPortThreads}",
                newMaxCompletionPortThreads);

            return;
        }

        VhLogger.Instance.LogInformation(
            "MaxWorkerThreads: {MaxWorkerThreads}, MaxCompletionPortThreads: {newMaxCompletionPortThreads}",
            maxWorkerThreads, newMaxCompletionPortThreads);
    }

    private static int GetMinCompletionPortThreads()
    {
        ThreadPool.GetMinThreads(out _, out var minCompletionPortThreads);
        return minCompletionPortThreads;
    }
}