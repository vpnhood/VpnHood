using System.Net;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Test;

public class TestAccessManager(string storagePath, FileAccessManagerOptions options)
    : FileAccessManager(storagePath, options)
{
    private readonly object _lockeObject = new();
    public int SessionGetCounter { get; private set; }
    public DateTime? LastConfigureTime { get; private set; }
    public ServerInfo? LastServerInfo { get; private set; }
    public ServerStatus? LastServerStatus { get; private set; }
    public IPEndPoint? RedirectHostEndPoint { get; set; }
    public Dictionary<string, IPEndPoint?> ServerLocations { get; set; } = new();


    public override async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        var ret = await base.Server_UpdateStatus(serverStatus);
        LastServerStatus = serverStatus;
        return ret;
    }

    public override Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        LastConfigureTime = DateTime.Now;
        LastServerInfo = serverInfo;
        LastServerStatus = serverInfo.Status;
        return base.Server_Configure(serverInfo);
    }

    public override Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        lock (_lockeObject)
            SessionGetCounter++;

        return base.Session_Get(sessionId, hostEndPoint, clientIp);
    }

    public override async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var ret = await base.Session_Create(sessionRequestEx);

        if (!sessionRequestEx.AllowRedirect)
            return ret;

        if (RedirectHostEndPoint != null &&
            !sessionRequestEx.HostEndPoint.Equals(RedirectHostEndPoint))
        {
            ret.RedirectHostEndPoint = RedirectHostEndPoint;
            ret.ErrorCode = SessionErrorCode.RedirectHost;
        }

        // manage region
        if (sessionRequestEx.ServerLocation != null)
        {
            var redirectEndPoint = ServerLocations[sessionRequestEx.ServerLocation];
            if (!sessionRequestEx.HostEndPoint.Equals(redirectEndPoint))
            {
                ret.RedirectHostEndPoint = ServerLocations[sessionRequestEx.ServerLocation];
                ret.ErrorCode = SessionErrorCode.RedirectHost;
            }
        }
        
        return ret;
    }
}