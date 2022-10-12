using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.ServerUtils;

public static class ServerUtil
{
    public static ServerState GetServerState(Models.Server server, ServerStatusEx? serverStatus, TimeSpan lostServerThreshold)
    {
        if (server.ConfigureTime == null) return ServerState.NotInstalled;
        if (serverStatus == null || serverStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) return ServerState.Lost;
        if (server.ConfigCode != server.LastConfigCode) return ServerState.Configuring;
        if (!server.IsEnabled) return ServerState.Disabled;
        if (serverStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }
    
    public static bool IsServerReady(Models.Server server, TimeSpan lostServerThreshold)
    {
        var serverState = GetServerState(server, server.ServerStatus, lostServerThreshold);
        return serverState is ServerState.Idle or ServerState.Active;
    }
}