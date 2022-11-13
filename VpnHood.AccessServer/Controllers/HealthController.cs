using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;
// ReSharper disable NotAccessedField.Local

[Route("/api/v{version:apiVersion}/health")]
[AllowAnonymous]
public class HealthController : SuperController<HealthController>
{
    private readonly AgentCacheClient _agentCacheClient;
    public HealthController(ILogger<HealthController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService, AgentCacheClient agentCacheClient) 
        : base(logger, vhContext, multilevelAuthService)
    {
        _agentCacheClient = agentCacheClient;
    }

    [HttpGet]
    public async Task Get()
    {
        await Task.Delay(0);
        //await _agentCacheClient.GetServers(Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69"));
    }
}