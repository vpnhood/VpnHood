using System.Net;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Managers.Http;
using VpnHood.Server.Access.Messaging;
using VpnHood.Tunneling;

namespace VpnHood.Test;

public class TestAccessManager : IAccessManager
{
    private readonly object _lockeObject = new();
    private readonly HttpAccessManager _httpAccessManager;
    public int SessionGetCounter { get; private set; }

    public TestAccessManager(IAccessManager baseAccessManager)
    {
        BaseAccessManager = baseAccessManager;
        EmbedIoAccessManager = new TestEmbedIoAccessManager(baseAccessManager);
        _httpAccessManager = new HttpAccessManager(new HttpAccessManagerOptions(EmbedIoAccessManager.BaseUri, "Bearer"))
        {
            Logger = VhLogger.Instance,
            LoggerEventId = GeneralEventId.AccessManager
        };
    }

    public DateTime? LastConfigureTime { get; private set; }
    public ServerInfo? LastServerInfo { get; private set; }
    public ServerStatus? LastServerStatus { get; private set; }

    public TestEmbedIoAccessManager EmbedIoAccessManager { get; }
    public IAccessManager BaseAccessManager { get; }

    public bool IsMaintenanceMode => _httpAccessManager.IsMaintenanceMode;
    public string ServerLocation { get; set; }

    public async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        var ret = await _httpAccessManager.Server_UpdateStatus(serverStatus);
        LastServerStatus = serverStatus;
        return ret;
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        LastConfigureTime = DateTime.Now;
        LastServerInfo = serverInfo;
        LastServerStatus = serverInfo.Status;
        return _httpAccessManager.Server_Configure(serverInfo);
    }

    public Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        lock (_lockeObject)
            SessionGetCounter++;

        return _httpAccessManager.Session_Get(sessionId, hostEndPoint, clientIp);
    }

    public async Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        var ret = await _httpAccessManager.Session_Create(sessionRequestEx);
        ret.ServerLocation = ServerLocation;
        return ret;
    }

    public Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData)
    {
        return _httpAccessManager.Session_AddUsage(sessionId, traffic, adData);
    }

    public Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic)
    {
        return _httpAccessManager.Session_Close(sessionId, traffic);
    }

    public void Dispose()
    {
        _httpAccessManager.Dispose();
        EmbedIoAccessManager.Dispose();
        BaseAccessManager.Dispose();
        GC.SuppressFinalize(this);
    }
}