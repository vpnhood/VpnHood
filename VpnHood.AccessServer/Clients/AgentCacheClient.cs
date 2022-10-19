using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using GrayMint.Common.Client;

namespace VpnHood.AccessServer.Clients;

public class AgentCacheClient : ApiClientBase
{
    public AgentCacheClient(IHttpClientFactory httpClientFactory) 
        : base(httpClientFactory.CreateClient(AppOptions.AgentHttpClientName))
    {
    }

    public Task InvalidateProject(Guid projectId)
    {
        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate", null, null);
    }

    public Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        return HttpPostAsync<Dtos.Server[]>($"/api/cache/projects/{projectId}/servers", null, null);
    }

    public Task InvalidateServer(Guid serverId)
    {
        return HttpPostAsync($"/api/cache/servers/{serverId}/invalidate", null, null);
    }

    public Task<Dtos.Session> GetSession(long sessionId)
    {
        return HttpGetAsync<Dtos.Session>($"/api/cache/sessions/{sessionId}/servers");
    }

    public Task Flush()
    {
        return HttpPostAsync($"/api/cache/flush", null, null);
    }
}