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
    [HttpGet("Foo")]
    [AllowAnonymous]
    public async Task<string> Foo()
    {
        await cacheService.GetServer(Guid.Parse("c3520778-dba2-4b70-a91d-a602b000734d"));

        await Task.Delay(0);
        logger.LogInformation("");
        logger.LogInformation("_______________________");
        logger.LogInformation("Information log");
        logger.LogWarning("Warning log");
        //_logger.LogError("Error log");
        return "OK";
    }
}
