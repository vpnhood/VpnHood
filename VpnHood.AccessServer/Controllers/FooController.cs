using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/foo")]
[AllowAnonymous]
public class FooController : SuperController<FooController>
{
    private readonly AgentCacheClient _agentCacheClient;
    public FooController(ILogger<FooController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService, AgentCacheClient agentCacheClient) 
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