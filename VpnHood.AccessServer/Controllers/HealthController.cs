using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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

    [HttpGet("{check}")]
    [AllowAnonymous]
    public async Task<string> Get(string check)
    {
        await Task.Delay(3000);
        return check;
    }
}