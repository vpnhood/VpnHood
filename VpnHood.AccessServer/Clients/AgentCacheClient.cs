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
        var parameters = new Dictionary<string, object?>
        {
            { "projectId", projectId }
        };

        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate", parameters, null);
    }

    public Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "projectId", projectId }
        };
        return HttpPostAsync<Dtos.Server[]>($"/api/cache/projects/{projectId}/servers", parameters, null);
    }

    public Task InvalidateServer(Guid serverId)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "serverId", serverId }
        };

        return HttpPostAsync($"/api/cache/servers/{serverId}/invalidate", parameters, null);
    }

}