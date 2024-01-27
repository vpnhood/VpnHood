using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;
// ReSharper disable UnusedMember.Local
// ReSharper disable NotAccessedField.Local

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[AllowAnonymous]
[Route("/api/v{version:apiVersion}/health")]
public class HealthController(
    ILogger<HealthController> logger,
    VhContext vhContext,
    AgentCacheClient agentCacheClient)
    : ControllerBase
{
    private readonly AgentCacheClient _agentCacheClient = agentCacheClient;
    private readonly ILogger<HealthController> _logger = logger;
    private readonly VhContext _vhContext = vhContext;

    [HttpGet("{check}")]
    [AllowAnonymous]
    public async Task<string> Get(string check)
    {
        await Task.Delay(3000);
        return check;
    }
}