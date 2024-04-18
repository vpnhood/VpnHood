using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;

// ReSharper disable NotAccessedField.Local

namespace VpnHood.AccessServer.Agent.Controllers;

public class HealthController(
    CacheService cacheService,
    ILogger<SystemController> logger)
    : ControllerBase
{
    [HttpGet("Status")]
    [AllowAnonymous]
    public async Task<string> Status() //it is foo
    {
        _ = cacheService;
        _ = logger;
        await Task.Delay(0);
        return "OK";
    }
}
