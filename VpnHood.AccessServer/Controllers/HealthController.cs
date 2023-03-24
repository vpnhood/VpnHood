using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;
// ReSharper disable NotAccessedField.Local

[ApiController]
[AllowAnonymous]
[Route("/api/v{version:apiVersion}/health")]
public class HealthController : ControllerBase
{
    private readonly AgentCacheClient _agentCacheClient;
    private readonly ILogger<HealthController> _logger;
    private readonly VhContext _vhContext;
    public HealthController(
        ILogger<HealthController> logger, 
        VhContext vhContext, 
        AgentCacheClient agentCacheClient)
    {
        _logger = logger;
        _vhContext = vhContext;
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