using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Utils;

public static class ServerUtil
{
    public static readonly Version MinClientVersion = Version.Parse("2.3.289"); 
    public static readonly Version MinServerVersion = Version.Parse("3.0.411"); 

    public static ServerState GetServerState(ServerModel server, ServerStatusModel? serverStatus, TimeSpan lostServerThreshold)
    {
        if (server.ConfigureTime == null) return ServerState.NotInstalled;
        if (serverStatus == null || serverStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) 
            return ServerState.Lost;
        if (server.ConfigCode != server.LastConfigCode) return ServerState.Configuring;
        if (!server.IsEnabled) return ServerState.Disabled;
        if (serverStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }

    public static bool IsServerReady(ServerModel server, ServerStatusModel? serverStatus, TimeSpan lostServerThreshold)
    {
        var serverState = GetServerState(server, serverStatus, lostServerThreshold);
        return serverState is ServerState.Idle or ServerState.Active;
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