
namespace VpnHood.AccessServer.ServerUtils;

public static class ServerUtil
{
    public static readonly Version MinServerVersion = Version.Parse("2.4.301");

    public static ServerState GetServerState(Models.Server server, TimeSpan lostServerThreshold)
    {
        if (server.ConfigureTime == null) return ServerState.NotInstalled;
        if (server.ServerStatus == null || server.ServerStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold) return ServerState.Lost;
        if (server.LastConfigError != null) return ServerState.Error;
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