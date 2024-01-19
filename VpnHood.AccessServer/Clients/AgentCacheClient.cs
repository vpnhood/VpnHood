using System.Text.Json;
using GrayMint.Common.Client;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Clients;

public class AgentCacheClient : ApiClientBase
{
    public AgentCacheClient(IHttpClientFactory httpClientFactory) 
        : base(httpClientFactory.CreateClient(AppOptions.AgentHttpClientName))
    {
        JsonSerializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }

    public Task InvalidateProject(Guid projectId)
    {
        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate", null, null);
    }

    public Task InvalidateProjectServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null, Guid? certificateId = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(serverFarmId)] = serverFarmId,
            [nameof(certificateId)] = certificateId,
            [nameof(serverProfileId)] = serverProfileId,
        };

        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate-servers", parameters, null);
    }

    public Task<VpnServer[]> GetServers(Guid projectId)
    {
        return HttpGetAsync<VpnServer[]>($"/api/cache/projects/{projectId}/servers");
    }

    public Task<VpnServer?> GetServer(Guid serverId)
    {
        return HttpGetAsync<VpnServer?>($"/api/cache/servers/{serverId}");
    }

    public Task InvalidateServer(Guid serverId)
    {
        return HttpPostAsync($"/api/cache/servers/{serverId}/invalidate", null, null);
    }

    public Task<Session> GetSession(long sessionId)
    {
        return HttpGetAsync<Session>($"/api/cache/sessions/{sessionId}");
    }

    public Task InvalidateSessions()
    {
        return HttpPostAsync("/api/cache/sessions/invalidate", null, null);
    }

    public Task Flush()
    {
        return HttpPostAsync("/api/cache/flush", null, null);
    }
}