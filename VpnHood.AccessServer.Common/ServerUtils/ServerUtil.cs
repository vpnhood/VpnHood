
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.ServerUtils;

public static class ServerUtil
{
    public static readonly Version MinServerVersion = Version.Parse("2.4.301");

    public static ServerState GetServerState(ServerModel serverModel, TimeSpan lostServerThreshold)
    {
        if (serverModel.ConfigureTime == null) return ServerState.NotInstalled;
        if (serverModel.ServerStatus == null || serverModel.ServerStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) return ServerState.Lost;
        if (serverModel.ConfigCode != serverModel.LastConfigCode) return ServerState.Configuring;
        if (!serverModel.IsEnabled) return ServerState.Disabled;
        if (serverModel.ServerStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }

    public static bool IsServerReady(ServerModel serverModel, TimeSpan lostServerThreshold)
    {
        var serverState = GetServerState(serverModel, lostServerThreshold);
        return serverState is ServerState.Idle or ServerState.Active;
    }

    public static void UpdateByCache(Dtos.Server[] servers, Dtos.Server[] cachedServers)
    {
        foreach (var server in servers)
        {
            var cachedServer = cachedServers.SingleOrDefault(x => x.ServerId == server.ServerId);
            if (cachedServer == null) continue;

            server.ServerStatus = cachedServer.ServerStatus;
            server.ServerState = cachedServer.ServerState;
        }
    }

    public static int GetBestTcpBufferSize(long? totalMemory)
    {
        if (totalMemory == null)
            return 8192;

        var bufferSize = (long)Math.Round((double)totalMemory / 0x80000000) * 4096;
        bufferSize = Math.Max(bufferSize, 8192);
        bufferSize = Math.Min(bufferSize, 8192); //81920, it looks it doesn't have effect
        return (int)bufferSize;
    }
}