using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.AccessServers;
using VpnHood.Server.Messaging;

#nullable enable
namespace VpnHood.Test;

public class TestAccessServer : IAccessServer
{
    private readonly RestAccessServer _restAccessServer;

    public TestAccessServer(IAccessServer baseAccessServer)
    {
        BaseAccessServer = baseAccessServer;
        EmbedIoAccessServer = new TestEmbedIoAccessServer(baseAccessServer);
        _restAccessServer = new RestAccessServer(new RestAccessServerOptions(EmbedIoAccessServer.BaseUri.AbsoluteUri, "Bearer"));
    }

    public DateTime? LastConfigureTime { get; private set; }
    public ServerInfo? LastServerInfo { get; private set; }
    public ServerStatus? LastServerStatus { get; private set; }

    public TestEmbedIoAccessServer EmbedIoAccessServer { get; }
    public IAccessServer BaseAccessServer { get; }

    public bool IsMaintenanceMode => _restAccessServer.IsMaintenanceMode;

    public async Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        var ret = await  _restAccessServer.Server_UpdateStatus(serverStatus);
        LastServerStatus = serverStatus;
        return ret;
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        LastConfigureTime = DateTime.Now;
        LastServerInfo = serverInfo;
        LastServerStatus = serverInfo.Status;
        return _restAccessServer.Server_Configure(serverInfo);
    }

    public Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        return _restAccessServer.Session_Get(sessionId, hostEndPoint, clientIp);
    }

    public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        return _restAccessServer.Session_Create(sessionRequestEx);
    }

    public Task<ResponseBase> Session_AddUsage(uint sessionId, bool closeSession, UsageInfo usageInfo)
    {
        return _restAccessServer.Session_AddUsage(sessionId, closeSession, usageInfo);
    }

    public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
    {
        return _restAccessServer.GetSslCertificateData(hostEndPoint);
    }

    public void Dispose()
    {
        _restAccessServer.Dispose();
        EmbedIoAccessServer.Dispose();
        BaseAccessServer.Dispose();
        GC.SuppressFinalize(this);
    }
}