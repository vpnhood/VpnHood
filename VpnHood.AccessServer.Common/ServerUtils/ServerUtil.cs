using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.ServerUtils;

public static class ServerUtil
{
    public static ServerState GetServerState(Models.Server server, TimeSpan lostServerThreshold)
    {
        if (server.ConfigureTime == null) return ServerState.NotInstalled;
        if (server.ServerStatus == null || server.ServerStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) return ServerState.Lost;
        if (server.ConfigCode != server.LastConfigCode) return ServerState.Configuring;
        if (!server.IsEnabled) return ServerState.Disabled;
        if (server.ServerStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }

    public static bool IsServerReady(Models.Server server, TimeSpan lostServerThreshold)
    {
        var serverState = GetServerState(server, lostServerThreshold);
        return serverState is ServerState.Idle or ServerState.Active;
    }
}