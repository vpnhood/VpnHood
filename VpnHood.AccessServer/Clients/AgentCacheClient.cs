using System.Text.Json;
using GrayMint.Common.Client;
using VpnHood.AccessServer.Persistence.Caches;

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

    public Task InvalidateProjectServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(serverFarmId)] = serverFarmId,
            [nameof(serverProfileId)] = serverProfileId
        };

        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate-servers", parameters, null);
    }

    public Task<ServerCache[]> GetServers(Guid projectId)
    {
        return HttpGetAsync<ServerCache[]>($"/api/cache/projects/{projectId}/servers");
    }

    public Task<ServerCache?> GetServer(Guid serverId)
    {
        return HttpGetAsync<ServerCache?>($"/api/cache/servers/{serverId}");
    }

    public Task InvalidateServer(Guid serverId)
    {
        return HttpPostAsync($"/api/cache/servers/{serverId}/invalidate", null, null);
    }

    public Task<SessionCache> GetSession(long sessionId)
    {
        return HttpGetAsync<SessionCache>($"/api/cache/sessions/{sessionId}");
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