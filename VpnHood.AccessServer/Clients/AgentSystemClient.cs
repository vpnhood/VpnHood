using System.Text.Json;
using GrayMint.Common.ApiClients;
using VpnHood.AccessServer.Options;

namespace VpnHood.AccessServer.Clients;

public class AgentSystemClient : ApiClientBase
{
    public AgentSystemClient(IHttpClientFactory httpClientFactory)
        : base(httpClientFactory.CreateClient(AppOptions.AgentHttpClientName))
    {
        JsonSerializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }

    public Task<string> GetServerAgentAuthorization(Guid serverId)
    {
        return HttpGetAsync<string>($"/api/system/servers/{serverId}/agent-authorization");
    }
}