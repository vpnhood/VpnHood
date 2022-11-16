﻿
namespace VpnHood.AccessServer.ServerUtils;

public static class ServerUtil
{
    public static readonly Version MinServerVersion = Version.Parse("2.4.301");

    public static ServerState GetServerState(Models.ServerModel serverModel, TimeSpan lostServerThreshold)
    {
        if (serverModel.ConfigureTime == null) return ServerState.NotInstalled;
        if (serverModel.ServerStatus == null || serverModel.ServerStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) return ServerState.Lost;
        if (serverModel.ConfigCode != serverModel.LastConfigCode) return ServerState.Configuring;
        if (!serverModel.IsEnabled) return ServerState.Disabled;
        if (serverModel.ServerStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }

    public static bool IsServerReady(Models.ServerModel serverModel, TimeSpan lostServerThreshold)
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
}