using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GrayMint.Common.Client;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test;

public class AgentClient : ApiClientBase
{
    public AgentClient(HttpClient httpClient) : base(httpClient)
    {
    }

    public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        return HttpPostAsync<SessionResponseEx>("sessions", null, sessionRequestEx);
    }

    public Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        var parameters = new Dictionary<string, object?>()
        {
            { "sessionId",  sessionId},
            { "hostEndPoint", hostEndPoint},
            { "clientIp",  clientIp}
        };

        return HttpGetAsync<SessionResponseEx>($"sessions/{sessionId}", parameters);
    }

    public Task<ResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo, bool closeSession = false)
    {
        var parameters = new Dictionary<string, object?>()
        {
            { "sessionId",  sessionId},
            { "closeSession",  closeSession}
        };

        return HttpPostAsync<ResponseBase>($"sessions/{sessionId}/usage", parameters, usageInfo);
    }

    public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
    {
        return HttpGetAsync<byte[]>($"certificates/{hostEndPoint}");
    }

    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        return HttpPostAsync<ServerCommand>("status", null, serverStatus);
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        return HttpPostAsync<ServerConfig>("configure", null, serverInfo);
    }

}