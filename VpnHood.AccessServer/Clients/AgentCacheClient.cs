using System.Text.Json;
using GrayMint.Common.Client;
using VpnHood.AccessServer.Options;
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

    public Task InvalidateServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null, Guid? serverId = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(projectId)] = projectId,
            [nameof(serverFarmId)] = serverFarmId,
            [nameof(serverProfileId)] = serverProfileId,
            [nameof(serverId)] = serverId
        };

        return HttpPostAsync("/api/cache/servers/invalidate", parameters, null);
    }

    public Task<ServerCache[]> GetServers(Guid projectId, Guid? serverFarmId = null)
    {
        var parameters = new Dictionary<string, object?>
        {
            [nameof(projectId)] = projectId,
            [nameof(serverFarmId)] = serverFarmId
        };

        return HttpGetAsync<ServerCache[]>("/api/cache/servers", parameters);
    }

    public Task<ServerCache?> GetServer(Guid serverId)
    {
        return HttpGetAsync<ServerCache?>($"/api/cache/servers/{serverId}");
    }


    public Task InvalidateServerFarm(Guid serverFarmId)
    {
        return HttpPostAsync($"/api/cache/server-farms/{serverFarmId}/invalidate", null, null);
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