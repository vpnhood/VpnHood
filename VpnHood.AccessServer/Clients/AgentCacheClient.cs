using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using GrayMint.Common.Client;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Clients;

public class AgentCacheClient : ApiClientBase
{
    public AgentCacheClient(IHttpClientFactory httpClientFactory) 
        : base(httpClientFactory.CreateClient(AppOptions.AgentHttpClientName))
    {
    }

    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        var serializerSettings = base.CreateSerializerSettings();
        serializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        return serializerSettings;
    }

    public Task InvalidateProject(Guid projectId)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["invalidateServers"] = true
        };
        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate", parameters, null);
    }

    public Task InvalidateProjectServers(Guid projectId)
    {
        return HttpPostAsync($"/api/cache/projects/{projectId}/invalidate-servers", null, null);
    }

    public Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        return HttpGetAsync<Dtos.Server[]>($"/api/cache/projects/{projectId}/servers");
    }

    public Task<Dtos.Server?> GetServer(Guid serverId)
    {
        return HttpGetAsync<Dtos.Server?>($"/api/cache/servers/{serverId}");
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