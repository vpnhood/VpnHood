using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GrayMint.Common.Client;

namespace VpnHood.AccessServer.Clients;

public class AgentSystemClient : ApiClientBase
{
    public AgentSystemClient(IHttpClientFactory httpClientFactory)
        : base(httpClientFactory.CreateClient(AppOptions.AgentHttpClientName))
    {
    }

    public Task<string> GetAgentAuthorization(Guid serverId)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "serverId", serverId }
        };

        return HttpGetAsync<string>("/api/system/agent-authorization", parameters);
    }
}