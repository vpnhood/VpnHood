using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Server.Utils;

internal static class ServerUtil
{
    public static int GetFreeUdpPort(AddressFamily addressFamily, int? start)
    {
        start ??= new Random().Next(1024, 9000);
        for (var port = start.Value; port < start + 1000; port++) {
            try {
                using var udpClient = new UdpClient(port, addressFamily);
                return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
            }
            catch (SocketException ex) when (ex.SocketErrorCode != SocketError.AddressAlreadyInUse) {
                break;
            }
            catch {
                // try the next port
            }
        }

        // try any port
        try {
            using var udpClient = new UdpClient(0, addressFamily);
            return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
        }
        catch {
            return 0;
        }
    }

    public static void ConfigMinIoThreads(int? minCompletionPortThreads)
    {
        // Configure thread pool size
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var newMinCompletionPortThreads);
        minCompletionPortThreads ??= newMinCompletionPortThreads * 30;
        if (minCompletionPortThreads != 0) newMinCompletionPortThreads = minCompletionPortThreads.Value;
        ThreadPool.SetMinThreads(minWorkerThreads, newMinCompletionPortThreads);
        VhLogger.Instance.LogInformation(
            "MinWorkerThreads: {MinWorkerThreads}, MinCompletionPortThreads: {newMinCompletionPortThreads}",
            minWorkerThreads, newMinCompletionPortThreads);
    }

    public static void ConfigMaxIoThreads(int? maxCompletionPortThreads)
    {
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var newMaxCompletionPortThreads);
        maxCompletionPortThreads ??= 0xFFFF; // We prefer all IO operations get slow together than be queued
        if (maxCompletionPortThreads != 0) newMaxCompletionPortThreads = maxCompletionPortThreads.Value;
        ThreadPool.SetMaxThreads(maxWorkerThreads, newMaxCompletionPortThreads);
        VhLogger.Instance.LogInformation(
            "MaxWorkerThreads: {MaxWorkerThreads}, MaxCompletionPortThreads: {newMaxCompletionPortThreads}",
            maxWorkerThreads, newMaxCompletionPortThreads);
    }
}